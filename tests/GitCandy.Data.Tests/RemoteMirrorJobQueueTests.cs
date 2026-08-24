using GitCandy.Application;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Remotes;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class RemoteMirrorJobQueueTests
{
    [TestMethod]
    public async Task EnqueueAsync_WithTriggerDuringLease_PreservesNewGenerationAfterCompletion()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        await fixture.Queue.EnqueueAsync(fixture.MirrorId, RemoteMirrorJobTrigger.Push);
        var lease = (await fixture.Queue.AcquireAsync(
            "worker-a",
            1,
            TimeSpan.FromMinutes(10))).Single();

        await fixture.Queue.EnqueueAsync(fixture.MirrorId, RemoteMirrorJobTrigger.Push);
        await fixture.Queue.CompleteAsync(
            lease.JobId,
            "worker-a",
            new RemoteMirrorJobCompletion(
                lease.RequestedGeneration,
                true,
                false,
                false,
                null,
                TimeSpan.Zero));

        var job = (await fixture.Queue.GetForRepositoryAsync(fixture.RepositoryId, 10)).Single();
        Assert.AreEqual(RemoteMirrorJobState.Pending, job.State);
        var nextLease = (await fixture.Queue.AcquireAsync(
            "worker-b",
            1,
            TimeSpan.FromMinutes(10))).Single();
        Assert.IsTrue(nextLease.RequestedGeneration > lease.RequestedGeneration);
    }

    [TestMethod]
    public async Task AcquireAsync_WithTwoWorkers_LeasesMirrorOnlyOnce()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        await fixture.Queue.EnqueueAsync(fixture.MirrorId, RemoteMirrorJobTrigger.Manual);

        var first = await fixture.Queue.AcquireAsync("worker-a", 1, TimeSpan.FromMinutes(10));
        var second = await fixture.SecondQueue.AcquireAsync("worker-b", 1, TimeSpan.FromMinutes(10));

        Assert.AreEqual(1, first.Count);
        Assert.AreEqual(0, second.Count);
    }

    [TestMethod]
    public async Task AcquireAsync_WithExpiredLease_RecoversJobAfterRestart()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        await fixture.Queue.EnqueueAsync(fixture.MirrorId, RemoteMirrorJobTrigger.Initial);
        _ = await fixture.Queue.AcquireAsync("stopped-worker", 1, TimeSpan.FromSeconds(30));
        fixture.Time.Advance(TimeSpan.FromMinutes(1));

        var recovered = (await fixture.SecondQueue.AcquireAsync(
            "restarted-worker",
            1,
            TimeSpan.FromMinutes(10))).Single();

        Assert.IsTrue((recovered.Triggers & RemoteMirrorJobTrigger.Recovery) != 0);
        Assert.AreEqual(2, recovered.AttemptCount);
    }

    [TestMethod]
    public async Task CompleteAsync_WithRetryDelay_DoesNotLeaseBeforeBackoffExpires()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        await fixture.Queue.EnqueueAsync(fixture.MirrorId, RemoteMirrorJobTrigger.Schedule);
        var lease = (await fixture.Queue.AcquireAsync(
            "worker-a",
            1,
            TimeSpan.FromMinutes(10))).Single();
        await fixture.Queue.CompleteAsync(
            lease.JobId,
            "worker-a",
            new RemoteMirrorJobCompletion(
                lease.RequestedGeneration,
                false,
                true,
                false,
                "remote_network_failed",
                TimeSpan.FromMinutes(2)));

        Assert.AreEqual(0, (await fixture.Queue.AcquireAsync(
            "worker-b",
            1,
            TimeSpan.FromMinutes(10))).Count);
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.AreEqual(1, (await fixture.SecondQueue.AcquireAsync(
            "worker-b",
            1,
            TimeSpan.FromMinutes(10))).Count);
    }

    [TestMethod]
    public async Task EnqueueRecoveryCandidatesAsync_WithMirrorMissingJob_QueuesInitialRecovery()
    {
        await using var fixture = await QueueFixture.CreateAsync();

        Assert.AreEqual(1, await fixture.Queue.EnqueueRecoveryCandidatesAsync(10));

        var lease = (await fixture.Queue.AcquireAsync(
            "worker-a",
            1,
            TimeSpan.FromMinutes(10))).Single();
        Assert.IsTrue((lease.Triggers & RemoteMirrorJobTrigger.Initial) != 0);
        Assert.IsTrue((lease.Triggers & RemoteMirrorJobTrigger.Recovery) != 0);
    }

    [TestMethod]
    public async Task EnqueueRecoveryCandidatesAsync_WithUnqueuedPushRef_QueuesPushRecovery()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        await fixture.Queue.EnqueueAsync(fixture.MirrorId, RemoteMirrorJobTrigger.Initial);
        var initialLease = (await fixture.Queue.AcquireAsync(
            "worker-a",
            1,
            TimeSpan.FromMinutes(10))).Single();
        await fixture.Queue.CompleteAsync(
            initialLease.JobId,
            "worker-a",
            new RemoteMirrorJobCompletion(
                initialLease.RequestedGeneration,
                true,
                false,
                false,
                null,
                TimeSpan.Zero));
        await using (var dbContext = new GitCandyDbContext(fixture.Options))
        {
            var mirror = await dbContext.RepositoryMirrors.SingleAsync(item => item.Id == fixture.MirrorId);
            mirror.Direction = RemoteMirrorDirection.Push;
            mirror.Authority = RemoteMirrorAuthority.GitCandy;
            dbContext.RemoteMirrorRefUpdates.Add(new GitCandyRemoteMirrorRefUpdate
            {
                MirrorId = fixture.MirrorId,
                ReferenceName = "refs/heads/main",
                OldObjectId = new string('0', 40),
                NewObjectId = new string('1', 40),
                Generation = 1,
                UpdatedAtUtc = fixture.Time.GetUtcNow().UtcDateTime
            });
            await dbContext.SaveChangesAsync();
        }

        Assert.AreEqual(1, await fixture.Queue.EnqueueRecoveryCandidatesAsync(10));

        var recovered = (await fixture.Queue.AcquireAsync(
            "worker-b",
            1,
            TimeSpan.FromMinutes(10))).Single();
        Assert.IsTrue((recovered.Triggers & RemoteMirrorJobTrigger.Push) != 0);
        Assert.IsTrue((recovered.Triggers & RemoteMirrorJobTrigger.Recovery) != 0);
    }

    [TestMethod]
    public async Task AcquireAsync_WithExpiredCanceledLease_DoesNotRecoverJob()
    {
        await using var fixture = await QueueFixture.CreateAsync();
        await fixture.Queue.EnqueueAsync(fixture.MirrorId, RemoteMirrorJobTrigger.Manual);
        var lease = (await fixture.Queue.AcquireAsync(
            "worker-a",
            1,
            TimeSpan.FromSeconds(30))).Single();
        Assert.IsTrue(await fixture.Queue.RequestCancellationAsync(lease.JobId));
        fixture.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.AreEqual(0, (await fixture.SecondQueue.AcquireAsync(
            "worker-b",
            1,
            TimeSpan.FromMinutes(10))).Count);
        var job = (await fixture.Queue.GetForRepositoryAsync(fixture.RepositoryId, 10)).Single();
        Assert.AreEqual(RemoteMirrorJobState.Canceled, job.State);
        Assert.AreEqual(RemoteMirrorErrorCodes.Canceled, job.LastErrorCode);
    }

    private sealed class QueueFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private QueueFixture(
            string databasePath,
            DbContextOptions<GitCandyDbContext> options,
            AdjustableTimeProvider time,
            RemoteMirrorJobQueue queue,
            RemoteMirrorJobQueue secondQueue,
            long repositoryId,
            long mirrorId)
        {
            _databasePath = databasePath;
            Options = options;
            Time = time;
            Queue = queue;
            SecondQueue = secondQueue;
            RepositoryId = repositoryId;
            MirrorId = mirrorId;
        }

        public AdjustableTimeProvider Time { get; }
        public DbContextOptions<GitCandyDbContext> Options { get; }
        public RemoteMirrorJobQueue Queue { get; }
        public RemoteMirrorJobQueue SecondQueue { get; }
        public long RepositoryId { get; }
        public long MirrorId { get; }

        public static async Task<QueueFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"gitcandy-mirror-jobs-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<GitCandyDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False", sqlite =>
                    sqlite.MigrationsAssembly("GitCandy.Data.Sqlite"))
                .Options;
            await using var dbContext = new GitCandyDbContext(options);
            await dbContext.Database.MigrateAsync();
            var user = new GitCandyUser
            {
                Id = "mirror-job-owner",
                UserName = "mirror-job-owner",
                NormalizedUserName = "MIRROR-JOB-OWNER",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            var repository = new GitCandyRepository
            {
                NamespaceId = GitCandyNamespace.LegacyNamespaceId,
                StorageName = "mirror-job-repository",
                Name = "mirror-job-repository",
                Description = string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.Users.Add(user);
            dbContext.Repositories.Add(repository);
            await dbContext.SaveChangesAsync();
            var connection = new GitCandyRemoteAccountConnection
            {
                OwnerKind = RemoteConnectionOwnerKind.User,
                OwnerUserId = user.Id,
                Provider = RemoteProviderKind.GitHub,
                ServerUrl = "https://github.com/",
                ExternalAccountId = "job-account",
                AccountKind = RemoteAccountKind.User,
                Login = "job-owner",
                AuthenticationKind = RemoteAuthenticationKind.App,
                CredentialReference = "vault:job",
                GrantedScopes = "[\"repo\"]",
                IsEnabled = true,
                Status = RemoteConnectionStatus.Healthy,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            dbContext.RemoteAccountConnections.Add(connection);
            await dbContext.SaveChangesAsync();
            var mirror = new GitCandyRepositoryMirror
            {
                RepositoryId = repository.Id,
                ConnectionId = connection.Id,
                RemoteRepositoryId = "job-repository",
                RemoteOwnerLogin = "upstream",
                RemoteRepositoryName = "job-repository",
                RemoteGitUrl = "https://github.com/upstream/job-repository.git",
                Direction = RemoteMirrorDirection.Pull,
                Authority = RemoteMirrorAuthority.Remote,
                RefFilterKind = RemoteMirrorRefFilterKind.AllRefs,
                ScheduleIntervalMinutes = 15,
                ScheduleTimeZone = "UTC",
                ScheduleEnabled = true,
                DivergencePolicy = RemoteMirrorDivergencePolicy.Stop,
                IsEnabled = true,
                Status = RemoteMirrorStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            dbContext.RepositoryMirrors.Add(mirror);
            await dbContext.SaveChangesAsync();

            var time = new AdjustableTimeProvider(DateTimeOffset.Parse("2026-08-24T00:00:00Z"));
            var factory = new TestDbContextFactory(options);
            return new QueueFixture(
                databasePath,
                options,
                time,
                new RemoteMirrorJobQueue(factory, time),
                new RemoteMirrorJobQueue(factory, time),
                repository.Id,
                mirror.Id);
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<GitCandyDbContext> options)
        : IDbContextFactory<GitCandyDbContext>
    {
        public GitCandyDbContext CreateDbContext() => new(options);

        public Task<GitCandyDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
