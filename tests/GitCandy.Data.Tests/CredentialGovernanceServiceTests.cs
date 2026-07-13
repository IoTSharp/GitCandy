using GitCandy.Application;
using GitCandy.Credentials;
using GitCandy.Configuration;
using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Data.Sqlite;
using GitCandy.Governance;
using GitCandy.Integrations;
using GitCandy.Ssh;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class CredentialGovernanceServiceTests
{
    [TestMethod]
    public async Task PersonalAccessToken_CreateAuthenticateAndRevoke_StoresOnlyHashAndAuditsLifecycle()
    {
        await using var fixture = await CredentialFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IPersonalAccessTokenService>();

        var created = await service.CreateAsync(
            fixture.OwnerId,
            "build agent",
            [PersonalAccessTokenScopes.GitWrite],
            DateTimeOffset.UtcNow.AddHours(1));

        Assert.IsNotNull(created);
        StringAssert.StartsWith(created.Token, "gcpat_");
        CollectionAssert.AreEquivalent(
            new[] { PersonalAccessTokenScopes.GitRead, PersonalAccessTokenScopes.GitWrite },
            created.Summary.Scopes.ToArray());
        var stored = await fixture.DbContext.PersonalAccessTokens.AsNoTracking().SingleAsync();
        Assert.AreNotEqual(created.Token, stored.TokenHash);
        Assert.IsFalse(stored.TokenHash.Contains(created.Token, StringComparison.Ordinal));

        var principal = await service.AuthenticateAsync(created.Token);

        Assert.IsNotNull(principal);
        Assert.AreEqual(fixture.OwnerId, principal.UserId);
        Assert.IsTrue(principal.Scopes.Contains(PersonalAccessTokenScopes.GitWrite));
        Assert.IsTrue(await service.RevokeAsync(fixture.OwnerId, created.Summary.Id));
        Assert.IsNull(await service.AuthenticateAsync(created.Token));
        var actions = await fixture.DbContext.CredentialAuditEvents.AsNoTracking()
            .OrderBy(item => item.Id)
            .Select(item => item.Action)
            .ToArrayAsync();
        CollectionAssert.AreEqual(new[] { "create", "authenticate", "revoke" }, actions);
    }

    [TestMethod]
    public async Task DeployKey_ReadOnlyRepositoryCredential_RestrictsRepositoryWriteAndRevokesAuthentication()
    {
        await using var fixture = await CredentialFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IDeployKeyService>();
        var keyBytes = new byte[] { 0, 0, 0, 7, 115, 115, 104, 45, 114, 115, 97, 1, 2, 3, 4 };
        var publicKey = "ssh-rsa " + Convert.ToBase64String(keyBytes) + " fixture";

        var created = await service.CreateAsync(
            fixture.RepositoryId,
            fixture.OwnerId,
            "read mirror",
            publicKey,
            canWrite: false,
            DateTimeOffset.UtcNow.AddHours(1));

        Assert.IsNotNull(created);
        var access = fixture.Services.GetRequiredService<ISshAccessService>();
        var principal = await access.AuthenticateAsync("ssh-rsa", keyBytes);
        Assert.IsNotNull(principal);
        Assert.AreEqual(created.Id, principal.DeployKeyId);
        Assert.IsTrue(await access.CanAccessRepositoryAsync(principal, fixture.RepositoryId, requiresWrite: false));
        Assert.IsFalse(await access.CanAccessRepositoryAsync(principal, fixture.RepositoryId, requiresWrite: true));
        Assert.IsFalse(await access.CanAccessRepositoryAsync(principal, fixture.OtherRepositoryId, requiresWrite: false));

        Assert.IsTrue(await service.RevokeAsync(
            fixture.RepositoryId,
            created.Id,
            fixture.OwnerId));
        Assert.IsNull(await access.AuthenticateAsync("ssh-rsa", keyBytes));
    }

    [TestMethod]
    public async Task DeployKey_WithUserKeyFingerprint_ReturnsNullAndPreservesGlobalFingerprintClaim()
    {
        await using var fixture = await CredentialFixture.CreateAsync();
        var keyBytes = new byte[] { 10, 20, 30, 40 };
        var publicKey = "ssh-rsa " + Convert.ToBase64String(keyBytes);
        var fingerprint = await fixture.Services.GetRequiredService<IUserAdministrationService>()
            .AddSshKeyAsync("owner", publicKey);
        Assert.IsNotNull(fingerprint);

        var created = await fixture.Services.GetRequiredService<IDeployKeyService>().CreateAsync(
            fixture.RepositoryId,
            fixture.OwnerId,
            "duplicate",
            publicKey,
            canWrite: true,
            expiresAt: null);

        Assert.IsNull(created);
        var claim = await fixture.DbContext.SshFingerprintClaims.AsNoTracking().SingleAsync();
        Assert.AreEqual(fingerprint, claim.Fingerprint);
        Assert.AreEqual(CredentialClaimTypes.UserSshKey, claim.CredentialKind);
        Assert.AreEqual(0, await fixture.DbContext.DeployKeys.CountAsync());
    }

    [TestMethod]
    public async Task PushGate_ProtectedMain_EnforcesOwnerForceDeleteAndMergeRulesWithAudit()
    {
        await using var fixture = await CredentialFixture.CreateAsync();
        var rules = fixture.Services.GetRequiredService<IBranchProtectionService>();
        var gate = fixture.Services.GetRequiredService<IGitPushGate>();
        var saved = await rules.SaveAsync(
            fixture.RepositoryId,
            fixture.OwnerId,
            new BranchProtectionEdit(
                null,
                "main",
                BranchAccessLevel.RepositoryOwner,
                BranchAccessLevel.RepositoryOwner,
                AllowForcePushes: false,
                AllowDeletions: false,
                AllowAdministratorBypass: false));
        Assert.IsNotNull(saved);

        var regular = new GitRefUpdate(new string('a', 40), new string('b', 40), "refs/heads/main");
        var collaboratorPush = await gate.EvaluateAsync(new GitPushGateRequest(
            fixture.RepositoryId,
            new GitRefActor("collaborator", fixture.CollaboratorId),
            GitRefOperation.Push,
            [regular]));
        var ownerPush = await gate.EvaluateAsync(new GitPushGateRequest(
            fixture.RepositoryId,
            new GitRefActor("owner", fixture.OwnerId),
            GitRefOperation.Push,
            [regular]));
        var forcePush = await gate.EvaluateAsync(new GitPushGateRequest(
            fixture.RepositoryId,
            new GitRefActor("owner", fixture.OwnerId),
            GitRefOperation.Push,
            [regular with { IsForceUpdate = true }]));
        var delete = await gate.EvaluateAsync(new GitPushGateRequest(
            fixture.RepositoryId,
            new GitRefActor("owner", fixture.OwnerId),
            GitRefOperation.WebDelete,
            [regular with { NewObjectId = new string('0', 40) }]));
        var collaboratorMerge = await gate.EvaluateAsync(new GitPushGateRequest(
            fixture.RepositoryId,
            new GitRefActor("collaborator", fixture.CollaboratorId),
            GitRefOperation.Merge,
            [regular]));

        Assert.IsFalse(collaboratorPush.Allowed);
        Assert.IsTrue(ownerPush.Allowed);
        Assert.IsFalse(forcePush.Allowed);
        Assert.IsFalse(delete.Allowed);
        Assert.IsFalse(collaboratorMerge.Allowed);
        Assert.AreEqual(4, await fixture.DbContext.GovernanceAuditEvents.CountAsync(item => item.Action == "gate.reject"));
    }

    [TestMethod]
    public async Task PushGate_WithRequiredCheck_UsesExactTargetShaAndLatestIdempotentState()
    {
        await using var fixture = await CredentialFixture.CreateAsync();
        var rules = fixture.Services.GetRequiredService<IBranchProtectionService>();
        var checks = fixture.Services.GetRequiredService<ICommitCheckService>();
        var gate = fixture.Services.GetRequiredService<IGitPushGate>();
        var oldSha = new string('a', 40);
        var targetSha = new string('b', 40);
        var savedRule = await rules.SaveAsync(
            fixture.RepositoryId,
            fixture.OwnerId,
            new BranchProtectionEdit(
                null,
                "main",
                BranchAccessLevel.RepositoryOwner,
                BranchAccessLevel.RepositoryOwner,
                AllowForcePushes: false,
                AllowDeletions: false,
                AllowAdministratorBypass: false,
                RequiredChecks: ["ci/build"]));
        Assert.IsNotNull(savedRule);
        Assert.IsNotNull(await checks.UpsertAsync(
            fixture.RepositoryId,
            fixture.OwnerId,
            credentialId: 1,
            new CommitCheckUpdate(
                oldSha,
                CommitCheckKind.Status,
                "ci/build",
                CommitCheckState.Success,
                "old head passed",
                TargetUrl: null,
                ExternalId: null)));

        var update = new GitRefUpdate(oldSha, targetSha, "refs/heads/main");
        var missing = await gate.EvaluateAsync(new GitPushGateRequest(
            fixture.RepositoryId,
            new GitRefActor("owner", fixture.OwnerId),
            GitRefOperation.Push,
            [update]));
        Assert.IsFalse(missing.Allowed);
        StringAssert.Contains(missing.Reasons.Single(), "is missing");

        Assert.IsNotNull(await checks.UpsertAsync(
            fixture.RepositoryId,
            fixture.OwnerId,
            credentialId: 1,
            new CommitCheckUpdate(
                targetSha,
                CommitCheckKind.Status,
                "ci/build",
                CommitCheckState.Pending,
                "running",
                TargetUrl: null,
                ExternalId: "build-1")));
        var pending = await gate.EvaluateAsync(new GitPushGateRequest(
            fixture.RepositoryId,
            new GitRefActor("owner", fixture.OwnerId),
            GitRefOperation.Push,
            [update]));
        Assert.IsFalse(pending.Allowed);
        StringAssert.Contains(pending.Reasons.Single(), "has not succeeded");

        Assert.IsNotNull(await checks.UpsertAsync(
            fixture.RepositoryId,
            fixture.OwnerId,
            credentialId: 1,
            new CommitCheckUpdate(
                targetSha,
                CommitCheckKind.Status,
                "ci/build",
                CommitCheckState.Success,
                "passed",
                TargetUrl: null,
                ExternalId: "build-1")));
        var allowed = await gate.EvaluateAsync(new GitPushGateRequest(
            fixture.RepositoryId,
            new GitRefActor("owner", fixture.OwnerId),
            GitRefOperation.Push,
            [update]));

        Assert.IsTrue(allowed.Allowed);
        Assert.AreEqual(2, await fixture.DbContext.CommitChecks.CountAsync());
        var current = await checks.GetForCommitAsync(fixture.RepositoryId, targetSha);
        Assert.AreEqual(CommitCheckState.Success, current.Single().State);
        var updatedRule = await rules.SaveAsync(
            fixture.RepositoryId,
            fixture.OwnerId,
            new BranchProtectionEdit(
                savedRule.Id,
                "main",
                BranchAccessLevel.RepositoryOwner,
                BranchAccessLevel.RepositoryOwner,
                AllowForcePushes: false,
                AllowDeletions: false,
                AllowAdministratorBypass: false,
                RequiredChecks: ["security/scan"]));
        Assert.IsNotNull(updatedRule);
        CollectionAssert.AreEqual(new[] { "security/scan" }, updatedRule.RequiredChecks.ToArray());
    }

    [TestMethod]
    public async Task Webhook_OutboxFailureRetryAndReplay_PreservesSecretAndVersionedPayload()
    {
        await using var fixture = await CredentialFixture.CreateAsync();
        var webhooks = fixture.Services.GetRequiredService<IWebhookService>();
        var created = await webhooks.CreateSubscriptionAsync(
            fixture.RepositoryId,
            fixture.OwnerId,
            new CreateWebhookSubscription(
                "external ci",
                "https://ci.example.test/hooks/gitcandy",
                WebhookEventTypes.Push));
        Assert.IsNotNull(created);
        Assert.IsFalse(created.Secret.Contains("protected:", StringComparison.Ordinal));
        var stored = await fixture.DbContext.WebhookSubscriptions.AsNoTracking().SingleAsync();
        Assert.AreNotEqual(created.Secret, stored.ProtectedSecret);
        StringAssert.StartsWith(stored.ProtectedSecret, "protected:");

        await fixture.Services.GetRequiredService<IIntegrationEventPublisher>().PublishPushAsync(
            "primary",
            "owner",
            new string('c', 64),
            [new IntegrationReference("refs/heads/main", new string('d', 40))]);
        var integrationEvent = await fixture.DbContext.IntegrationEvents.AsNoTracking().SingleAsync();
        using var payload = System.Text.Json.JsonDocument.Parse(integrationEvent.PayloadJson);
        Assert.AreEqual(1, payload.RootElement.GetProperty("version").GetInt32());
        Assert.AreEqual("push", payload.RootElement.GetProperty("type").GetString());
        Assert.AreEqual("owner/primary", payload.RootElement.GetProperty("repository").GetProperty("fullName").GetString());

        var firstClaim = await webhooks.ClaimDueDeliveriesAsync(10, TimeSpan.FromMinutes(1));
        Assert.AreEqual(1, firstClaim.Count);
        Assert.AreEqual(1, firstClaim[0].AttemptCount);
        await webhooks.CompleteDeliveryAttemptAsync(
            firstClaim[0].DeliveryId,
            new WebhookSendResult(false, 503, "http_503"),
            DateTimeOffset.UtcNow.AddSeconds(-1));
        var secondClaim = await webhooks.ClaimDueDeliveriesAsync(10, TimeSpan.FromMinutes(1));
        Assert.AreEqual(1, secondClaim.Count);
        Assert.AreEqual(2, secondClaim[0].AttemptCount);
        await webhooks.CompleteDeliveryAttemptAsync(
            secondClaim[0].DeliveryId,
            new WebhookSendResult(true, 204),
            nextAttemptAt: null);
        var replayId = await webhooks.ReplayDeliveryAsync(
            fixture.RepositoryId,
            secondClaim[0].DeliveryId,
            fixture.OwnerId);

        Assert.IsNotNull(replayId);
        var deliveries = await webhooks.GetDeliveriesAsync(fixture.RepositoryId);
        Assert.AreEqual(2, deliveries.Count);
        Assert.IsTrue(deliveries.Any(item => item.State == WebhookDeliveryState.Succeeded));
        Assert.IsTrue(deliveries.Any(item => item.ReplayOfDeliveryId == secondClaim[0].DeliveryId));
    }

    private sealed class CredentialFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly string _databasePath;

        private CredentialFixture(
            ServiceProvider provider,
            AsyncServiceScope scope,
            string databasePath,
            string ownerId,
            string collaboratorId,
            long repositoryId,
            long otherRepositoryId)
        {
            _provider = provider;
            _scope = scope;
            _databasePath = databasePath;
            OwnerId = ownerId;
            CollaboratorId = collaboratorId;
            RepositoryId = repositoryId;
            OtherRepositoryId = otherRepositoryId;
        }

        public IServiceProvider Services => _scope.ServiceProvider;
        public GitCandyDbContext DbContext => Services.GetRequiredService<GitCandyDbContext>();
        public string OwnerId { get; }
        public string CollaboratorId { get; }
        public long RepositoryId { get; }
        public long OtherRepositoryId { get; }

        public static async Task<CredentialFixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), "GitCandy.Tests", $"{Guid.NewGuid():N}.db");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "sqlite",
                ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());
            services.AddGitCandyApplicationServices();
            services.Configure<WebhookOptions>(_ => { });
            services.AddSingleton<IWebhookSecretProtector, TestWebhookSecretProtector>();
            services.AddSingleton<IOutboundTargetPolicy, AllowAllOutboundTargetPolicy>();
            services.AddIdentityCore<GitCandyUser>()
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<GitCandyDbContext>();
            var provider = services.BuildServiceProvider(validateScopes: true);
            var scope = provider.CreateAsyncScope();
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
                await db.Database.EnsureCreatedAsync();
                var owner = NewUser("owner");
                var collaborator = NewUser("collaborator");
                db.Users.AddRange(owner, collaborator);
                var ownerNamespace = new GitCandyNamespace
                {
                    OwnerType = NamespaceOwnerType.User,
                    UserId = owner.Id,
                    Slug = "owner",
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Namespaces.Add(ownerNamespace);
                await db.SaveChangesAsync();
                var repository = NewRepository(ownerNamespace.Id, "primary");
                var otherRepository = NewRepository(ownerNamespace.Id, "other");
                repository.UserRoles.Add(new GitCandyUserRepositoryRole
                {
                    UserId = owner.Id, AllowRead = true, AllowWrite = true, IsOwner = true
                });
                repository.UserRoles.Add(new GitCandyUserRepositoryRole
                {
                    UserId = collaborator.Id, AllowRead = true, AllowWrite = true
                });
                db.Repositories.AddRange(repository, otherRepository);
                await db.SaveChangesAsync();
                return new CredentialFixture(
                    provider, scope, databasePath, owner.Id, collaborator.Id, repository.Id, otherRepository.Id);
            }
            catch
            {
                await scope.DisposeAsync();
                await provider.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }

        private static GitCandyUser NewUser(string name) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.com",
            NormalizedEmail = $"{name}@example.com".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        private static GitCandyRepository NewRepository(long namespaceId, string name) => new()
        {
            NamespaceId = namespaceId,
            Name = name,
            StorageName = name,
            Description = name,
            CreatedAtUtc = DateTime.UtcNow,
            IsPrivate = true
        };

        private sealed class TestWebhookSecretProtector : IWebhookSecretProtector
        {
            public string Protect(string secret) => $"protected:{secret}";
            public string Unprotect(string protectedSecret) => protectedSecret["protected:".Length..];
        }

        private sealed class AllowAllOutboundTargetPolicy : IOutboundTargetPolicy
        {
            public ValueTask<bool> IsAllowedAsync(Uri target, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(true);

            public ValueTask<Stream> ConnectAsync(
                DnsEndPoint endpoint,
                CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
        }
    }
}
