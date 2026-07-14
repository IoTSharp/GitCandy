using GitCandy.Application;
using GitCandy.Data;
using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Enterprise;
using GitCandy.Configuration;
using GitCandy.Teams;
using GitCandy.Web.Enterprise;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Tests;

[TestClass]
public sealed class ScimProvisioningTests
{
    [TestMethod]
    public async Task UpsertUserAsync_WithSameExternalId_UpdatesSingleIdentityAndDoesNotDuplicateUser()
    {
        await using var fixture = await ScimFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IScimProvisioningService>();

        var created = await service.UpsertUserAsync(
            fixture.ConnectionId,
            new ScimUserData("entra-object-1", "person", "first@example.com", "First Name", true));
        var updated = await service.UpsertUserAsync(
            fixture.ConnectionId,
            new ScimUserData("entra-object-1", "person.renamed", "changed@example.com", "Changed Name", true));

        Assert.IsTrue(created.Succeeded);
        Assert.IsTrue(created.Created);
        Assert.IsTrue(updated.Succeeded);
        Assert.IsFalse(updated.Created);
        Assert.AreEqual(created.Resource?.Id, updated.Resource?.Id);
        Assert.AreEqual("changed@example.com", updated.Resource?.Email);
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        Assert.AreEqual(1, await dbContext.EnterpriseExternalIdentities.CountAsync());
        Assert.AreEqual(2, await dbContext.Users.CountAsync());
        Assert.AreEqual(
            TeamRole.Member,
            (await dbContext.UserTeamRoles.SingleAsync(role => role.UserId != ScimFixture.OwnerId)).Role);
    }

    [TestMethod]
    public async Task PatchUserAsync_WithInactiveUser_LocksAccountRevokesMembershipAndRefreshesStamp()
    {
        await using var fixture = await ScimFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IScimProvisioningService>();
        var created = await service.UpsertUserAsync(
            fixture.ConnectionId,
            new ScimUserData("entra-object-2", "inactive", "inactive@example.com", "Inactive", true));
        Assert.IsNotNull(created.Resource);
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var identity = await dbContext.EnterpriseExternalIdentities.SingleAsync(
            item => item.Id == created.Resource.Id);
        var user = await dbContext.Users.SingleAsync(item => item.Id == identity.UserId);
        var oldStamp = user.SecurityStamp;

        var patched = await service.PatchUserAsync(
            fixture.ConnectionId,
            identity.Id,
            new ScimUserData(identity.ExternalId, identity.UserName, identity.Email, identity.DisplayName, false));

        Assert.IsTrue(patched.Succeeded);
        dbContext.ChangeTracker.Clear();
        user = await dbContext.Users.SingleAsync(item => item.Id == identity.UserId);
        Assert.AreEqual(DateTimeOffset.MaxValue, user.LockoutEnd);
        Assert.AreNotEqual(oldStamp, user.SecurityStamp);
        Assert.IsFalse(await dbContext.UserTeamRoles.AnyAsync(role => role.UserId == user.Id));
        Assert.IsFalse((await dbContext.EnterpriseExternalIdentities.SingleAsync()).IsActive);
    }

    [TestMethod]
    public async Task PatchUserAsync_WithLastTeamOwner_ReturnsConflictAndKeepsIdentityActive()
    {
        await using var fixture = await ScimFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IScimProvisioningService>();
        var created = await service.UpsertUserAsync(
            fixture.ConnectionId,
            new ScimUserData("entra-owner", "external-owner", "external-owner@example.com", null, true));
        Assert.IsNotNull(created.Resource);
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var externalIdentity = await dbContext.EnterpriseExternalIdentities.SingleAsync();
        var localOwner = await dbContext.UserTeamRoles.SingleAsync(role => role.UserId == ScimFixture.OwnerId);
        localOwner.Role = TeamRole.Member;
        var externalRole = await dbContext.UserTeamRoles.SingleAsync(role => role.UserId == externalIdentity.UserId);
        externalRole.Role = TeamRole.TeamOwner;
        await dbContext.SaveChangesAsync();

        var result = await service.PatchUserAsync(
            fixture.ConnectionId,
            externalIdentity.Id,
            new ScimUserData(
                externalIdentity.ExternalId,
                externalIdentity.UserName,
                externalIdentity.Email,
                externalIdentity.DisplayName,
                false));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("lastTeamOwner", result.ErrorCode);
        dbContext.ChangeTracker.Clear();
        Assert.IsTrue((await dbContext.EnterpriseExternalIdentities.SingleAsync()).IsActive);
    }

    [TestMethod]
    public async Task UpsertGroupAsync_WithProvisionedMembers_IsIdempotentAndReplacesMemberships()
    {
        await using var fixture = await ScimFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IScimProvisioningService>();
        var first = await service.UpsertUserAsync(
            fixture.ConnectionId,
            new ScimUserData("member-1", "member1", "member1@example.com", null, true));
        var second = await service.UpsertUserAsync(
            fixture.ConnectionId,
            new ScimUserData("member-2", "member2", "member2@example.com", null, true));
        Assert.IsNotNull(first.Resource);
        Assert.IsNotNull(second.Resource);

        var created = await service.UpsertGroupAsync(
            fixture.ConnectionId,
            new ScimGroupData("group-1", "Engineering", [first.Resource.Id, second.Resource.Id]));
        var updated = await service.UpsertGroupAsync(
            fixture.ConnectionId,
            new ScimGroupData("group-1", "Platform", [second.Resource.Id]));

        Assert.IsTrue(created.Created);
        Assert.IsFalse(updated.Created);
        Assert.AreEqual(created.Resource?.Id, updated.Resource?.Id);
        CollectionAssert.AreEqual(
            new[] { second.Resource.Id },
            updated.Resource?.MemberIds.ToArray());
    }

    [TestMethod]
    public async Task RotateAsync_WithNewBearer_InvalidatesPreviousBearer()
    {
        await using var fixture = await ScimFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IScimBearerService>();

        var first = await service.RotateAsync(
            fixture.ConnectionId,
            ScimFixture.OwnerId,
            actorIsSystemAdministrator: false);
        var second = await service.RotateAsync(
            fixture.ConnectionId,
            ScimFixture.OwnerId,
            actorIsSystemAdministrator: false);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.IsNull(await service.ValidateAsync(first.Token));
        Assert.AreEqual(fixture.ConnectionId, await service.ValidateAsync(second.Token));
        Assert.IsNull(await service.ValidateAsync(second.Token + "tampered"));
    }

    [TestMethod]
    public async Task TryRecordAsync_WithDuplicateProviderEvent_AcceptsOnlyFirstReceipt()
    {
        await using var fixture = await ScimFixture.CreateAsync();
        var service = fixture.Services.GetRequiredService<IEnterpriseEventReceiptService>();
        var payloadHash = new string('A', 64);

        Assert.IsTrue(await service.TryRecordAsync(fixture.ConnectionId, "event-1", payloadHash));
        Assert.IsFalse(await service.TryRecordAsync(fixture.ConnectionId, "event-1", payloadHash));

        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        Assert.AreEqual(1, await dbContext.EnterpriseProviderEvents.CountAsync());
    }

    [TestMethod]
    public async Task SynchronizeAsync_WithMissingUser_DeprovisionsCredentialsAndPersistsCompletion()
    {
        await using var fixture = await ScimFixture.CreateAsync();
        fixture.DirectoryProvider.Users =
        [
            new EnterpriseDirectoryUser("sync-1", "sync1", "sync1@example.com", "Sync One", true, []),
            new EnterpriseDirectoryUser("sync-2", "sync2", "sync2@example.com", "Sync Two", true, [])
        ];
        await using var firstScope = fixture.Services.CreateAsyncScope();
        var firstSync = firstScope.ServiceProvider.GetRequiredService<IEnterpriseDirectorySyncService>();
        var first = await firstSync.SynchronizeAsync(fixture.ConnectionId);
        Assert.IsTrue(first.Succeeded);

        var firstDb = firstScope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var removedIdentity = await firstDb.EnterpriseExternalIdentities.SingleAsync(
            item => item.ExternalId == "sync-2");
        firstDb.PersonalAccessTokens.Add(new GitCandyPersonalAccessToken
        {
            UserId = removedIdentity.UserId!,
            Name = "automation",
            TokenHash = new string('A', 64),
            TokenPrefix = "gc_test",
            Scopes = "api:read",
            CreatedAtUtc = DateTime.UtcNow
        });
        firstDb.SshKeys.Add(new GitCandySshKey
        {
            UserId = removedIdentity.UserId!,
            KeyType = "ssh-ed25519",
            Fingerprint = "SHA256:sync-user-key",
            PublicKey = "AAAAC3NzaC1lZDI1NTE5AAAAITest",
            ImportedAtUtc = DateTime.UtcNow
        });
        firstDb.SshFingerprintClaims.Add(new GitCandySshFingerprintClaim
        {
            Fingerprint = "SHA256:sync-user-key",
            CredentialKind = "user",
            ClaimedAtUtc = DateTime.UtcNow
        });
        await firstDb.SaveChangesAsync();

        fixture.DirectoryProvider.Users =
        [
            new EnterpriseDirectoryUser("sync-1", "sync1", "sync1@example.com", "Sync One", true, [])
        ];
        await using var secondScope = fixture.Services.CreateAsyncScope();
        var second = await secondScope.ServiceProvider
            .GetRequiredService<IEnterpriseDirectorySyncService>()
            .SynchronizeAsync(fixture.ConnectionId);

        Assert.IsTrue(second.Succeeded);
        Assert.AreEqual(1, second.UsersDeactivated);
        var dbContext = secondScope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var deprovisioned = await dbContext.EnterpriseExternalIdentities.SingleAsync(
            item => item.ExternalId == "sync-2");
        Assert.IsFalse(deprovisioned.IsActive);
        Assert.IsNotNull(deprovisioned.DeprovisionedAtUtc);
        Assert.IsNotNull((await dbContext.PersonalAccessTokens.SingleAsync()).RevokedAtUtc);
        Assert.AreEqual(0, await dbContext.SshKeys.CountAsync());
        Assert.AreEqual(0, await dbContext.SshFingerprintClaims.CountAsync());
        var connection = await dbContext.EnterpriseConnections.SingleAsync();
        Assert.AreEqual(EnterpriseConnectionStatus.Healthy, connection.Status);
        Assert.IsNotNull(connection.LastSynchronizedAtUtc);
    }

    [TestMethod]
    public async Task ResolveAsync_WithExistingEmail_ReturnsConflictWithoutAutomaticLink()
    {
        await using var fixture = await ScimFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = await scope.ServiceProvider.GetRequiredService<IEnterpriseConnectionService>()
            .GetRuntimeContextAsync(fixture.ConnectionId);
        Assert.IsNotNull(context);

        var result = await scope.ServiceProvider.GetRequiredService<IEnterpriseSignInService>()
            .ResolveAsync(
                context,
                new EnterpriseLoginIdentity(
                    "new-external-id",
                    "tenant-1",
                    "different-name",
                    "owner@example.com",
                    "Conflicting User"));

        Assert.AreEqual(EnterpriseSignInStatus.Conflict, result.Status);
        Assert.AreEqual("email_conflict", result.ErrorCode);
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        Assert.AreEqual(0, await dbContext.EnterpriseExternalIdentities.CountAsync());
    }

    private sealed class ScimFixture : IAsyncDisposable
    {
        public const string OwnerId = "local-break-glass-owner";
        private readonly SqliteConnection _connection;

        private ScimFixture(
            ServiceProvider services,
            SqliteConnection connection,
            long connectionId,
            MutableDirectoryProvider directoryProvider)
        {
            Services = services;
            _connection = connection;
            ConnectionId = connectionId;
            DirectoryProvider = directoryProvider;
        }

        public ServiceProvider Services { get; }
        public long ConnectionId { get; }
        public MutableDirectoryProvider DirectoryProvider { get; }

        public static async Task<ScimFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            var directoryProvider = new MutableDirectoryProvider();
            services.AddLogging();
            services.AddDbContextFactory<GitCandyDbContext>(options => options.UseSqlite(connection));
            services.AddIdentityCore<GitCandyUser>()
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<GitCandyDbContext>();
            services.Configure<GitCandyApplicationOptions>(options => options.AllowRegisterUser = true);
            services.AddSingleton<IEnterpriseSecretResolver, SyncSecretResolver>();
            services.AddGitCandyApplicationServices();
            services.AddSingleton<IEnterpriseProvider>(directoryProvider);
            services.AddScoped<IScimProvisioningService, ScimProvisioningService>();
            services.AddScoped<IEnterpriseDirectorySyncService, EnterpriseDirectorySyncService>();
            services.AddScoped<IEnterpriseSignInService, EnterpriseSignInService>();
            var provider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            var owner = new GitCandyUser
            {
                Id = OwnerId,
                UserName = "owner",
                NormalizedUserName = "OWNER",
                Email = "owner@example.com",
                NormalizedEmail = "OWNER@EXAMPLE.COM",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            var team = new GitCandyTeam
            {
                Name = "enterprise",
                DisplayName = "Enterprise",
                Description = string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
            team.UserRoles.Add(new GitCandyUserTeamRole { UserId = owner.Id, Role = TeamRole.TeamOwner });
            var enterpriseConnection = new GitCandyEnterpriseConnection
            {
                Team = team,
                Name = "SCIM",
                NormalizedName = "SCIM",
                Provider = EnterpriseProviderKind.Scim,
                ExternalOrganizationId = "tenant-1",
                SecretReference = "env:SCIM_UNUSED",
                ConfigurationJson = "{\"allowJit\":true}",
                LoginEnabled = true,
                ProvisioningEnabled = true,
                IsEnabled = true,
                Status = EnterpriseConnectionStatus.NotTested,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            dbContext.Users.Add(owner);
            dbContext.EnterpriseConnections.Add(enterpriseConnection);
            await dbContext.SaveChangesAsync();
            return new ScimFixture(provider, connection, enterpriseConnection.Id, directoryProvider);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    public sealed class MutableDirectoryProvider : IEnterpriseDirectoryProvider
    {
        public IReadOnlyList<EnterpriseDirectoryUser> Users { get; set; } = [];
        public EnterpriseProviderKind Kind => EnterpriseProviderKind.Scim;
        public EnterpriseProviderCapabilities Capabilities =>
            EnterpriseProviderCapabilities.DirectoryUsers | EnterpriseProviderCapabilities.DirectoryGroups;

        public Task<EnterpriseProviderDiagnostic> TestAsync(
            EnterpriseConnectionContext connection,
            EnterpriseSecret secret,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new EnterpriseProviderDiagnostic(true, "connected", "Connected."));

        public Task<EnterpriseDirectoryPage> GetDirectoryPageAsync(
            EnterpriseConnectionContext connection,
            EnterpriseSecret secret,
            string? cursor,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new EnterpriseDirectoryPage(Users, [], null));
    }

    private sealed class SyncSecretResolver : IEnterpriseSecretResolver
    {
        public ValueTask<EnterpriseSecret?> ResolveAsync(
            string secretReference,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<EnterpriseSecret?>(new("sync-secret"));
    }
}
