using System.Net;
using GitCandy.Application;
using GitCandy.Audit;
using GitCandy.Configuration;
using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Data.Sqlite;
using GitCandy.Integrations;
using GitCandy.Notifications;
using GitCandy.PullRequests;
using GitCandy.Releases;
using GitCandy.Search;
using GitCandy.Workspace;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class CollaborationExtensionServiceTests
{
    [TestMethod]
    public async Task NotificationDelivery_WithReviewPreferenceAndRevokedPermission_FailsClosedWithoutLeakingPayload()
    {
        await using var fixture = await CollaborationFixture.CreateAsync();
        var deliveries = fixture.Services.GetRequiredService<INotificationDeliveryService>();
        Assert.IsTrue(await deliveries.SavePreferenceAsync(
            fixture.ReviewerId,
            new NotificationPreferenceEdit(
                WorkspaceNotificationEventType.Review,
                EmailEnabled: false,
                WebhookEnabled: true,
                "https://notifications.example.test/review",
                "fixture-secret")));

        var inbox = await fixture.Services.GetRequiredService<IWorkspaceService>().GetNotificationsAsync(
            fixture.ReviewerId,
            isAdministrator: false,
            new WorkspaceNotificationQuery());
        Assert.AreEqual(1, inbox.Items.Count);
        Assert.AreEqual(WorkspaceNotificationEventType.Review, inbox.Items[0].EventType);
        Assert.AreEqual(1, await fixture.DbContext.NotificationDeliveries.CountAsync());

        var role = await fixture.DbContext.UserRepositoryRoles.SingleAsync(item =>
            item.RepositoryId == fixture.PrivateRepositoryId && item.UserId == fixture.ReviewerId);
        fixture.DbContext.UserRepositoryRoles.Remove(role);
        await fixture.DbContext.SaveChangesAsync();
        var claimed = await deliveries.ClaimDueAsync(10, TimeSpan.FromMinutes(1));
        var diagnostic = (await deliveries.GetDiagnosticsAsync(fixture.ReviewerId)).Single();

        Assert.AreEqual(0, claimed.Count);
        Assert.AreEqual(NotificationDeliveryState.Failed, diagnostic.State);
        Assert.AreEqual("permission_revoked", diagnostic.ErrorCode);
    }

    [TestMethod]
    public async Task AuditAndSearch_WithPrivateRepository_FiltersBeforeReturningCollaborationResults()
    {
        await using var fixture = await CollaborationFixture.CreateAsync();
        var audit = await fixture.Services.GetRequiredService<IAuditLogService>()
            .GetRepositoryEventsAsync(fixture.PrivateRepositoryId);
        Assert.IsTrue(audit.Any(item => item.Action == "rule.save" && item.Actor == "owner"));

        var search = fixture.Services.GetRequiredService<ICollaborationSearchService>();
        var anonymous = await search.SearchAsync(null, false, "fixture", SearchScope.All);
        var owner = await search.SearchAsync(fixture.OwnerId, false, "fixture", SearchScope.All);

        Assert.IsTrue(anonymous.Hits.Any(item => item.Repository == "owner/public"));
        Assert.IsFalse(anonymous.Hits.Any(item => item.Repository == "owner/private"));
        Assert.IsFalse(anonymous.Repositories.Any(item => item.RepositoryId == fixture.PrivateRepositoryId));
        Assert.IsTrue(owner.Hits.Any(item => item.Repository == "owner/private"));
    }

    [TestMethod]
    public async Task Release_WithTagAndBoundedAsset_StreamsStoresAuditsAndRejectsTraversalName()
    {
        await using var fixture = await CollaborationFixture.CreateAsync();
        var releases = fixture.Services.GetRequiredService<IReleaseService>();
        var release = await releases.CreateAsync(
            fixture.PrivateRepositoryId,
            fixture.OwnerId,
            new CreateRelease("v1.0.0", "Fixture release", "## Notes", IsDraft: false));
        Assert.IsNotNull(release);
        Assert.AreEqual(new string('a', 40), release.TagCommitSha);
        Assert.IsTrue(release.BodyHtml.Contains("<h2", StringComparison.Ordinal));

        await using var invalidContent = new MemoryStream([1, 2, 3]);
        Assert.IsNull(await releases.AddAssetAsync(
            fixture.PrivateRepositoryId,
            release.Id,
            fixture.OwnerId,
            "../secret.txt",
            "text/plain",
            invalidContent,
            invalidContent.Length));
        await using var content = new MemoryStream([1, 2, 3, 4]);
        var asset = await releases.AddAssetAsync(
            fixture.PrivateRepositoryId,
            release.Id,
            fixture.OwnerId,
            "artifact.bin",
            "application/octet-stream",
            content,
            content.Length);
        Assert.IsNotNull(asset);
        Assert.AreEqual(4, asset.Length);
        Assert.AreEqual(64, asset.Sha256.Length);
        await using var download = (await releases.OpenAssetAsync(
            fixture.PrivateRepositoryId,
            asset.Id,
            includeDrafts: true))!.Content;
        Assert.AreEqual(4, download.Length);
        Assert.IsTrue(await fixture.DbContext.GovernanceAuditEvents.AnyAsync(item =>
            item.RepositoryId == fixture.PrivateRepositoryId && item.Action == "release.asset-upload"));
    }

    private sealed class CollaborationFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly string _databasePath;

        private CollaborationFixture(
            ServiceProvider provider,
            AsyncServiceScope scope,
            string databasePath,
            string ownerId,
            string reviewerId,
            long privateRepositoryId)
        {
            _provider = provider;
            _scope = scope;
            _databasePath = databasePath;
            OwnerId = ownerId;
            ReviewerId = reviewerId;
            PrivateRepositoryId = privateRepositoryId;
        }

        public IServiceProvider Services => _scope.ServiceProvider;
        public GitCandyDbContext DbContext => Services.GetRequiredService<GitCandyDbContext>();
        public string OwnerId { get; }
        public string ReviewerId { get; }
        public long PrivateRepositoryId { get; }

        public static async Task<CollaborationFixture> CreateAsync()
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
            services.Configure<ReleaseOptions>(options =>
            {
                options.MaxAssetBytes = 1024;
                options.MaxTotalAssetBytes = 4096;
            });
            services.AddSingleton<IWebhookSecretProtector, TestSecretProtector>();
            services.AddSingleton<IOutboundTargetPolicy, AllowAllTargetPolicy>();
            services.AddSingleton<IReleaseTagResolver, TestTagResolver>();
            services.AddSingleton<IReleaseAssetStore, MemoryReleaseAssetStore>();
            services.AddSingleton<IIntegrationEventPublisher, NullIntegrationEventPublisher>();
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
                var reviewer = NewUser("reviewer");
                db.Users.AddRange(owner, reviewer);
                var ownerNamespace = new GitCandyNamespace
                {
                    OwnerType = NamespaceOwnerType.User,
                    UserId = owner.Id,
                    Slug = "owner",
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Namespaces.Add(ownerNamespace);
                await db.SaveChangesAsync();
                var privateRepository = NewRepository(ownerNamespace.Id, "private", isPrivate: true);
                privateRepository.UserRoles.Add(new GitCandyUserRepositoryRole
                {
                    UserId = owner.Id, AllowRead = true, AllowWrite = true, IsOwner = true
                });
                privateRepository.UserRoles.Add(new GitCandyUserRepositoryRole
                {
                    UserId = reviewer.Id, AllowRead = true, AllowWrite = true
                });
                var publicRepository = NewRepository(ownerNamespace.Id, "public", isPrivate: false);
                db.Repositories.AddRange(privateRepository, publicRepository);
                await db.SaveChangesAsync();
                db.Issues.AddRange(
                    NewIssue(privateRepository.Id, owner.Id, 1, "Private fixture issue"),
                    NewIssue(publicRepository.Id, owner.Id, 1, "Public fixture issue"));
                var pullRequest = NewPullRequest(privateRepository.Id, owner.Id);
                pullRequest.Reviewers.Add(new GitCandyPullRequestReviewer
                {
                    ReviewerUserId = reviewer.Id,
                    RequestedByUserId = owner.Id,
                    RequestedAtUtc = DateTime.UtcNow,
                    Version = 1
                });
                db.PullRequests.Add(pullRequest);
                db.GovernanceAuditEvents.Add(new GitCandyGovernanceAuditEvent
                {
                    RepositoryId = privateRepository.Id,
                    ActorUserId = owner.Id,
                    Action = "rule.save",
                    Outcome = "success",
                    ReferenceName = "main",
                    Detail = "fixture",
                    OccurredAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                return new CollaborationFixture(
                    provider, scope, databasePath, owner.Id, reviewer.Id, privateRepository.Id);
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
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N")
        };

        private static GitCandyRepository NewRepository(long namespaceId, string name, bool isPrivate) => new()
        {
            NamespaceId = namespaceId,
            Name = name,
            StorageName = name,
            Description = $"{name} fixture repository",
            CreatedAtUtc = DateTime.UtcNow,
            IsPrivate = isPrivate,
            AllowAnonymousRead = !isPrivate
        };

        private static GitCandyIssue NewIssue(long repositoryId, string ownerId, long number, string title) => new()
        {
            RepositoryId = repositoryId,
            Number = number,
            Title = title,
            BodyMarkdown = "fixture body",
            BodyHtml = "<p>fixture body</p>",
            AuthorUserId = ownerId,
            State = Issues.IssueState.Open,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Version = 1
        };

        private static GitCandyPullRequest NewPullRequest(long repositoryId, string ownerId) => new()
        {
            RepositoryId = repositoryId,
            SourceRepositoryId = repositoryId,
            SourceNamespaceSnapshot = "owner",
            SourceRepositorySnapshot = "private",
            Number = 2,
            Title = "Private fixture pull request",
            BodyMarkdown = "fixture body",
            BodyHtml = "<p>fixture body</p>",
            AuthorUserId = ownerId,
            SourceBranch = "feature",
            TargetBranch = "main",
            OriginalBaseSha = new string('0', 40),
            OriginalHeadSha = new string('a', 40),
            CurrentBaseSha = new string('0', 40),
            CurrentHeadSha = new string('a', 40),
            State = PullRequestState.Open,
            ActivePairKey = "open:fixture",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Version = 1
        };
    }

    private sealed class TestSecretProtector : IWebhookSecretProtector
    {
        public string Protect(string secret) => $"protected:{secret}";
        public string Unprotect(string protectedSecret) => protectedSecret["protected:".Length..];
    }

    private sealed class AllowAllTargetPolicy : IOutboundTargetPolicy
    {
        public ValueTask<bool> IsAllowedAsync(Uri target, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
        public ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestTagResolver : IReleaseTagResolver
    {
        public string? ResolveTag(string repositoryStorageName, string tagName, CancellationToken cancellationToken = default) =>
            string.Equals(tagName, "v1.0.0", StringComparison.Ordinal) ? new string('a', 40) : null;
    }

    private sealed class MemoryReleaseAssetStore : IReleaseAssetStore
    {
        private readonly Dictionary<string, byte[]> _assets = new(StringComparer.Ordinal);

        public async Task<StoredReleaseAsset?> StoreAsync(long repositoryId, long releaseId, string assetId,
            Stream content, long maxBytes, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            if (memory.Length > maxBytes) return null;
            var bytes = memory.ToArray();
            _assets[assetId] = bytes;
            return new StoredReleaseAsset(
                bytes.Length,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
        }

        public Task<Stream?> OpenReadAsync(long repositoryId, long releaseId, string assetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(_assets.TryGetValue(assetId, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(long repositoryId, long releaseId, string assetId,
            CancellationToken cancellationToken = default)
        {
            _assets.Remove(assetId);
            return Task.CompletedTask;
        }

        public Task<int> DeleteOrphansAsync(IReadOnlySet<string> activeAssetIds, DateTimeOffset olderThan,
            CancellationToken cancellationToken = default)
        {
            var orphanIds = _assets.Keys.Where(item => !activeAssetIds.Contains(item)).ToArray();
            foreach (var orphanId in orphanIds) _assets.Remove(orphanId);
            return Task.FromResult(orphanIds.Length);
        }
    }

    private sealed class NullIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public Task PublishPushAsync(string repositoryStorageName, string actorName, string repositoryStateId,
            IReadOnlyList<IntegrationReference> references, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishPullRequestMergedAsync(PullRequestMergedEvent mergedEvent,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishCheckUpdatedAsync(long repositoryId, string actorUserId, CommitCheckSummary check,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishReleasePublishedAsync(long repositoryId, string actorUserId, long releaseId, string tagName,
            string tagCommitSha, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
