using System.Security.Cryptography;
using System.Text;
using GitCandy.Application;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Remotes;
using GitCandy.Web.Remotes;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Tests;

[TestClass]
public sealed class RemoteProviderEventTests
{
    [TestMethod]
    public void IsValid_WithThreeProviderFixtures_ValidatesExpectedSignatureContract()
    {
        var validator = new RemoteProviderWebhookSignatureValidator();
        var secret = new RemoteSecret("fixture-webhook-secret");
        var payload = Encoding.UTF8.GetBytes("{\"fixture\":true}");

        var githubHeaders = new HeaderDictionary
        {
            ["X-Hub-Signature-256"] = $"sha256={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret.Value), payload)).ToLowerInvariant()}"
        };
        var gitlabHeaders = new HeaderDictionary { ["X-Gitlab-Token"] = secret.Value };
        var giteeHeaders = new HeaderDictionary { ["X-Gitee-Token"] = secret.Value };

        Assert.IsTrue(validator.IsValid(RemoteProviderKind.GitHub, secret, githubHeaders, payload));
        Assert.IsTrue(validator.IsValid(RemoteProviderKind.GitLab, secret, gitlabHeaders, payload));
        Assert.IsTrue(validator.IsValid(RemoteProviderKind.Gitee, secret, giteeHeaders, payload));
        githubHeaders["X-Hub-Signature-256"] = "sha256=" + new string('0', 64);
        gitlabHeaders["X-Gitlab-Token"] = "wrong";
        giteeHeaders["X-Gitee-Token"] = "wrong";
        Assert.IsFalse(validator.IsValid(RemoteProviderKind.GitHub, secret, githubHeaders, payload));
        Assert.IsFalse(validator.IsValid(RemoteProviderKind.GitLab, secret, gitlabHeaders, payload));
        Assert.IsFalse(validator.IsValid(RemoteProviderKind.Gitee, secret, giteeHeaders, payload));
    }

    [TestMethod]
    public async Task ProcessAsync_WithRenameAndDuplicate_UpdatesProfileAndQueuesPullOnce()
    {
        await using var fixture = await ProviderEventFixture.CreateAsync();
        var payload = Encoding.UTF8.GetBytes("""
            {
              "action": "renamed",
              "repository": {
                "id": 701,
                "full_name": "renamed-owner/renamed-repository",
                "html_url": "https://github.com/renamed-owner/renamed-repository",
                "default_branch": "main"
              }
            }
            """);
        var remoteEvent = new RemoteProviderEvent(
            fixture.ConnectionId,
            RemoteProviderKind.GitHub,
            "delivery-rename",
            "repository",
            payload);

        var first = await fixture.Service.ProcessAsync(remoteEvent);
        var duplicate = await fixture.Service.ProcessAsync(remoteEvent);

        Assert.IsTrue(first.Accepted);
        Assert.IsFalse(first.Duplicate);
        Assert.AreEqual(1, first.EnqueuedMirrorCount);
        Assert.IsTrue(duplicate.Duplicate);
        Assert.IsNotNull(fixture.MirrorService.UpdatedProfile);
        Assert.AreEqual("renamed-owner", fixture.MirrorService.UpdatedProfile.OwnerLogin);
        var jobs = await fixture.Queue.GetForRepositoryAsync(fixture.RepositoryId, 10);
        Assert.AreEqual(1, jobs.Count);
        Assert.IsTrue((jobs[0].Triggers & RemoteMirrorJobTrigger.Webhook) != 0);
    }

    [TestMethod]
    public async Task ProcessAsync_WithRemoteDelete_PausesMirrorAndCancelsPendingJob()
    {
        await using var fixture = await ProviderEventFixture.CreateAsync();
        await fixture.Queue.EnqueueAsync(fixture.MirrorId, RemoteMirrorJobTrigger.Schedule);
        var payload = Encoding.UTF8.GetBytes("""
            {
              "action": "deleted",
              "repository": { "id": 701 }
            }
            """);

        var result = await fixture.Service.ProcessAsync(new RemoteProviderEvent(
            fixture.ConnectionId,
            RemoteProviderKind.GitHub,
            "delivery-delete",
            "repository",
            payload));

        Assert.AreEqual("remote_deleted", result.Code);
        await using var dbContext = fixture.CreateDbContext();
        var mirror = await dbContext.RepositoryMirrors.AsNoTracking().SingleAsync();
        Assert.IsFalse(mirror.IsEnabled);
        Assert.AreEqual(RemoteMirrorStatus.Failed, mirror.Status);
        Assert.AreEqual("remote_repository_not_found", mirror.LastErrorCode);
        var job = (await fixture.Queue.GetForRepositoryAsync(fixture.RepositoryId, 10)).Single();
        Assert.AreEqual(RemoteMirrorJobState.Canceled, job.State);
    }

    [TestMethod]
    public async Task ProcessAsync_WithProfileUpdateFailure_DoesNotConsumeDeliveryReceipt()
    {
        await using var fixture = await ProviderEventFixture.CreateAsync();
        fixture.MirrorService.UpdateSucceeded = false;
        var payload = Encoding.UTF8.GetBytes("""
            {
              "action": "renamed",
              "repository": {
                "id": 701,
                "full_name": "renamed-owner/renamed-repository",
                "html_url": "https://github.com/renamed-owner/renamed-repository"
              }
            }
            """);
        var remoteEvent = new RemoteProviderEvent(
            fixture.ConnectionId,
            RemoteProviderKind.GitHub,
            "delivery-retryable-rename",
            "repository",
            payload);

        var failed = await fixture.Service.ProcessAsync(remoteEvent);
        fixture.MirrorService.UpdateSucceeded = true;
        var retried = await fixture.Service.ProcessAsync(remoteEvent);

        Assert.IsFalse(failed.Accepted);
        Assert.AreEqual("invalid_repository", failed.Code);
        Assert.IsTrue(retried.Accepted);
        Assert.IsFalse(retried.Duplicate);
        await using var dbContext = fixture.CreateDbContext();
        Assert.AreEqual(1, await dbContext.RemoteProviderEvents.CountAsync());
    }

    private sealed class ProviderEventFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<GitCandyDbContext> _options;

        private ProviderEventFixture(
            SqliteConnection connection,
            DbContextOptions<GitCandyDbContext> options,
            RemoteProviderEventService service,
            RemoteMirrorJobQueue queue,
            RecordingMirrorService mirrorService,
            long connectionId,
            long repositoryId,
            long mirrorId)
        {
            _connection = connection;
            _options = options;
            Service = service;
            Queue = queue;
            MirrorService = mirrorService;
            ConnectionId = connectionId;
            RepositoryId = repositoryId;
            MirrorId = mirrorId;
        }

        public RemoteProviderEventService Service { get; }
        public RemoteMirrorJobQueue Queue { get; }
        public RecordingMirrorService MirrorService { get; }
        public long ConnectionId { get; }
        public long RepositoryId { get; }
        public long MirrorId { get; }

        public GitCandyDbContext CreateDbContext() => new(_options);

        public static async Task<ProviderEventFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<GitCandyDbContext>()
                .UseSqlite(connection)
                .Options;
            await using var dbContext = new GitCandyDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            var user = new GitCandyUser
            {
                Id = "provider-event-owner",
                UserName = "provider-event-owner",
                NormalizedUserName = "PROVIDER-EVENT-OWNER",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            var repository = new GitCandyRepository
            {
                NamespaceId = GitCandyNamespace.LegacyNamespaceId,
                StorageName = "provider-event-repository",
                Name = "provider-event-repository",
                Description = string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.Users.Add(user);
            dbContext.Repositories.Add(repository);
            await dbContext.SaveChangesAsync();
            var remoteConnection = new GitCandyRemoteAccountConnection
            {
                OwnerKind = RemoteConnectionOwnerKind.User,
                OwnerUserId = user.Id,
                Provider = RemoteProviderKind.GitHub,
                ServerUrl = "https://github.com/",
                ExternalAccountId = "provider-event-account",
                AccountKind = RemoteAccountKind.User,
                Login = "provider-event-owner",
                AuthenticationKind = RemoteAuthenticationKind.App,
                CredentialReference = "vault:event",
                GrantedScopes = "[\"repo\"]",
                WebhookSecretReference = "env:GITCANDY_EVENT_FIXTURE",
                IsEnabled = true,
                Status = RemoteConnectionStatus.Healthy,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            dbContext.RemoteAccountConnections.Add(remoteConnection);
            await dbContext.SaveChangesAsync();
            var mirror = new GitCandyRepositoryMirror
            {
                RepositoryId = repository.Id,
                ConnectionId = remoteConnection.Id,
                RemoteRepositoryId = "701",
                RemoteOwnerLogin = "original-owner",
                RemoteRepositoryName = "original-repository",
                RemoteGitUrl = "https://github.com/original-owner/original-repository.git",
                Direction = RemoteMirrorDirection.Pull,
                Authority = RemoteMirrorAuthority.Remote,
                RefFilterKind = RemoteMirrorRefFilterKind.AllRefs,
                ScheduleIntervalMinutes = 15,
                ScheduleTimeZone = "UTC",
                ScheduleEnabled = true,
                DivergencePolicy = RemoteMirrorDivergencePolicy.Stop,
                IsEnabled = true,
                Status = RemoteMirrorStatus.Idle,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            dbContext.RepositoryMirrors.Add(mirror);
            await dbContext.SaveChangesAsync();

            var factory = new TestDbContextFactory(options);
            var queue = new RemoteMirrorJobQueue(factory, TimeProvider.System);
            var mirrorService = new RecordingMirrorService();
            var service = new RemoteProviderEventService(
                factory,
                mirrorService,
                queue,
                TimeProvider.System);
            return new ProviderEventFixture(
                connection,
                options,
                service,
                queue,
                mirrorService,
                remoteConnection.Id,
                repository.Id,
                mirror.Id);
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<GitCandyDbContext> options)
        : IDbContextFactory<GitCandyDbContext>
    {
        public GitCandyDbContext CreateDbContext() => new(options);

        public Task<GitCandyDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class RecordingMirrorService : IRemoteMirrorService
    {
        public RemoteRepositoryProfile? UpdatedProfile { get; private set; }

        public bool UpdateSucceeded { get; set; } = true;

        public Task<RemoteMirrorOperationResult> RegisterAsync(
            string actorUserId,
            RemoteMirrorRegistration registration,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RemoteMirrorOperationResult> SynchronizeAsync(
            long mirrorId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdateRemoteProfileAsync(
            long mirrorId,
            RemoteRepositoryProfile remoteRepository,
            CancellationToken cancellationToken = default)
        {
            UpdatedProfile = remoteRepository;
            return Task.FromResult(UpdateSucceeded);
        }
    }
}
