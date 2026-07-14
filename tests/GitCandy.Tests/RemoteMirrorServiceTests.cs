using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Git;
using GitCandy.Remotes;
using GitCandy.Web.Remotes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitCandy.Tests;

[TestClass]
public sealed class RemoteMirrorServiceTests
{
    [TestMethod]
    public async Task EnqueueAsync_WithRepeatedReference_MergesLatestEventAndIncrementsGeneration()
    {
        await using var fixture = await MirrorFixture.CreateAsync(RemoteMirrorDirection.Push);
        var sink = new RemoteMirrorPushEventSink(fixture.DbContextFactory, TimeProvider.System);
        var first = new RemoteMirrorRefEvent(
            new string('0', 40),
            new string('a', 40),
            "refs/heads/main");
        var second = new RemoteMirrorRefEvent(
            new string('a', 40),
            new string('b', 40),
            "refs/heads/main");

        await sink.EnqueueAsync(fixture.RepositoryId, [first]);
        await sink.EnqueueAsync(fixture.RepositoryId, [second]);

        await using var dbContext = fixture.CreateDbContext();
        var pending = await dbContext.RemoteMirrorRefUpdates.AsNoTracking().SingleAsync();
        Assert.AreEqual(2L, pending.Generation);
        Assert.AreEqual(second.OldObjectId, pending.OldObjectId);
        Assert.AreEqual(second.NewObjectId, pending.NewObjectId);
    }

    [TestMethod]
    public async Task SynchronizeAsync_WithPullDivergenceStop_PreservesLocalReferenceAndRecordsDivergence()
    {
        await using var fixture = await MirrorFixture.CreateAsync(RemoteMirrorDirection.Pull);
        fixture.References.LocalReferences["refs/heads/main"] = new string('b', 40);
        fixture.References.RemoteReferences["refs/heads/main"] = new string('c', 40);

        var result = await fixture.Service.SynchronizeAsync(fixture.MirrorId);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(RemoteMirrorStatus.Diverged, result.Status);
        Assert.AreEqual(RemoteMirrorErrorCodes.Diverged, result.ErrorCode);
        Assert.AreEqual(new string('b', 40), fixture.References.LocalReferences["refs/heads/main"]);
        await using var dbContext = fixture.CreateDbContext();
        var mirror = await dbContext.RepositoryMirrors.AsNoTracking().SingleAsync();
        Assert.AreEqual(RemoteMirrorStatus.Diverged, mirror.Status);
        Assert.AreEqual(new string('c', 40), mirror.LastObservedRemoteHead);
    }

    [TestMethod]
    public async Task SynchronizeAsync_WithPushOverwrite_UsesForceAndConsumesMergedRefEvent()
    {
        await using var fixture = await MirrorFixture.CreateAsync(
            RemoteMirrorDirection.Push,
            RemoteMirrorDivergencePolicy.OverwriteTarget);
        fixture.References.LocalReferences["refs/heads/main"] = new string('b', 40);
        fixture.References.RemoteReferences["refs/heads/main"] = new string('c', 40);
        await using (var dbContext = fixture.CreateDbContext())
        {
            dbContext.RemoteMirrorRefUpdates.Add(new GitCandyRemoteMirrorRefUpdate
            {
                MirrorId = fixture.MirrorId,
                ReferenceName = "refs/heads/main",
                OldObjectId = new string('a', 40),
                NewObjectId = new string('b', 40),
                Generation = 3,
                EnqueuedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }

        var result = await fixture.Service.SynchronizeAsync(fixture.MirrorId);

        Assert.IsTrue(
            result.Succeeded,
            $"status={result.Status}; error={result.ErrorCode}; requests={fixture.Backend.Requests.Count}");
        Assert.AreEqual(RemoteMirrorStatus.Succeeded, result.Status);
        var push = fixture.Backend.Requests.Single(item =>
            item.Operation == RemoteRepositorySyncOperation.Push);
        Assert.AreEqual(1, push.RefSpecs.Count);
        Assert.IsTrue(push.RefSpecs[0].Force);
        Assert.AreEqual("refs/heads/main", push.RefSpecs[0].SourceReference);
        await using var verification = fixture.CreateDbContext();
        Assert.AreEqual(0, await verification.RemoteMirrorRefUpdates.CountAsync());
        Assert.AreEqual(
            new string('b', 40),
            await verification.RepositoryMirrors.AsNoTracking()
                .Select(item => item.LastObservedRemoteHead)
                .SingleAsync());
        Assert.IsTrue(await verification.GovernanceAuditEvents.AsNoTracking()
            .AnyAsync(item => item.Action == "mirror.push.force"
                && item.ReferenceName == "refs/heads/main"));
    }

    [TestMethod]
    public async Task SynchronizeAsync_WithMoreThanBackendRefLimit_ConsumesBoundedBatchAndRemainsPending()
    {
        await using var fixture = await MirrorFixture.CreateAsync(RemoteMirrorDirection.Push);
        await using (var dbContext = fixture.CreateDbContext())
        {
            for (var index = 0; index < 1025; index++)
            {
                var referenceName = $"refs/heads/b{index:D4}";
                fixture.References.LocalReferences[referenceName] = new string('b', 40);
                dbContext.RemoteMirrorRefUpdates.Add(new GitCandyRemoteMirrorRefUpdate
                {
                    MirrorId = fixture.MirrorId,
                    ReferenceName = referenceName,
                    OldObjectId = new string('0', 40),
                    NewObjectId = new string('b', 40),
                    Generation = 1,
                    EnqueuedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow.AddTicks(index)
                });
            }
            await dbContext.SaveChangesAsync();
        }

        var result = await fixture.Service.SynchronizeAsync(fixture.MirrorId);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(RemoteMirrorStatus.Pending, result.Status);
        Assert.AreEqual(
            1024,
            fixture.Backend.Requests.Single(item => item.Operation == RemoteRepositorySyncOperation.Push)
                .RefSpecs.Count);
        await using var verification = fixture.CreateDbContext();
        Assert.AreEqual(1, await verification.RemoteMirrorRefUpdates.CountAsync());
    }

    private sealed class MirrorFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<GitCandyDbContext> _options;

        private MirrorFixture(
            SqliteConnection connection,
            DbContextOptions<GitCandyDbContext> options,
            RemoteMirrorService service,
            FakeReferenceStore references,
            FakeSyncBackend backend,
            long mirrorId,
            long repositoryId)
        {
            _connection = connection;
            _options = options;
            Service = service;
            References = references;
            Backend = backend;
            MirrorId = mirrorId;
            RepositoryId = repositoryId;
            DbContextFactory = new TestDbContextFactory(options);
        }

        public RemoteMirrorService Service { get; }
        public FakeReferenceStore References { get; }
        public FakeSyncBackend Backend { get; }
        public long MirrorId { get; }
        public long RepositoryId { get; }
        public IDbContextFactory<GitCandyDbContext> DbContextFactory { get; }

        public static async Task<MirrorFixture> CreateAsync(
            RemoteMirrorDirection direction,
            RemoteMirrorDivergencePolicy divergencePolicy = RemoteMirrorDivergencePolicy.Stop)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<GitCandyDbContext>()
                .UseSqlite(connection)
                .Options;
            await using var dbContext = new GitCandyDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            var user = new GitCandy.Data.Identity.GitCandyUser
            {
                Id = "mirror-owner",
                UserName = "mirror-owner",
                NormalizedUserName = "MIRROR-OWNER",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            var repository = new GitCandyRepository
            {
                NamespaceId = GitCandyNamespace.LegacyNamespaceId,
                StorageName = "mirror-service",
                Name = "mirror-service",
                Description = string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.Users.Add(user);
            dbContext.Repositories.Add(repository);
            await dbContext.SaveChangesAsync();
            var connectionEntity = new GitCandyRemoteAccountConnection
            {
                OwnerKind = RemoteConnectionOwnerKind.User,
                OwnerUserId = user.Id,
                Provider = RemoteProviderKind.GitHub,
                ServerUrl = "https://github.com/",
                ExternalAccountId = "mirror-account",
                AccountKind = RemoteAccountKind.User,
                Login = "mirror-owner",
                AuthenticationKind = RemoteAuthenticationKind.PersonalAccessToken,
                CredentialReference = "vault:mirror-service",
                GrantedScopes = "[\"repo\"]",
                IsEnabled = true,
                Status = RemoteConnectionStatus.Healthy,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            dbContext.RemoteAccountConnections.Add(connectionEntity);
            await dbContext.SaveChangesAsync();
            var mirror = new GitCandyRepositoryMirror
            {
                RepositoryId = repository.Id,
                ConnectionId = connectionEntity.Id,
                RemoteRepositoryId = "mirror-repository",
                RemoteOwnerLogin = "upstream",
                RemoteRepositoryName = "mirror-service",
                RemoteGitUrl = "https://github.com/upstream/mirror-service.git",
                Direction = direction,
                Authority = direction == RemoteMirrorDirection.Pull
                    ? RemoteMirrorAuthority.Remote
                    : RemoteMirrorAuthority.GitCandy,
                RefFilterKind = RemoteMirrorRefFilterKind.AllRefs,
                DivergencePolicy = divergencePolicy,
                Prune = true,
                IsEnabled = true,
                Status = RemoteMirrorStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            dbContext.RepositoryMirrors.Add(mirror);
            await dbContext.SaveChangesAsync();

            var factory = new TestDbContextFactory(options);
            var references = new FakeReferenceStore();
            var backend = new FakeSyncBackend();
            var provider = new FakeRemoteProvider();
            var service = new RemoteMirrorService(
                factory,
                new FakeProviderCatalog(provider),
                new FakeCredentialVault(),
                backend,
                references,
                new NoopPushEventSink(),
                new FakePathResolver(),
                TimeProvider.System,
                NullLogger<RemoteMirrorService>.Instance);
            return new MirrorFixture(
                connection,
                options,
                service,
                references,
                backend,
                mirror.Id,
                repository.Id);
        }

        public GitCandyDbContext CreateDbContext() => new(_options);

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<GitCandyDbContext> options)
        : IDbContextFactory<GitCandyDbContext>
    {
        public GitCandyDbContext CreateDbContext() => new(options);
    }

    private sealed class FakeReferenceStore : IRemoteMirrorReferenceStore
    {
        public Dictionary<string, string> LocalReferences { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> RemoteReferences { get; } = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, string> ReadReferences(
            GitRepositoryContext repository,
            string referencePrefix,
            CancellationToken cancellationToken = default)
        {
            if (referencePrefix.StartsWith(RemoteMirrorReferenceNamespace.Prefix, StringComparison.Ordinal))
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in RemoteReferences)
                {
                    var stagedName = item.Key.StartsWith("refs/heads/", StringComparison.Ordinal)
                        ? referencePrefix + "heads/" + item.Key["refs/heads/".Length..]
                        : referencePrefix + "tags/" + item.Key["refs/tags/".Length..];
                    result[stagedName] = item.Value;
                }
                return result;
            }

            return LocalReferences
                .Where(item => item.Key.StartsWith(referencePrefix, StringComparison.Ordinal))
                .ToDictionary(StringComparer.Ordinal);
        }

        public bool IsAncestor(
            GitRepositoryContext repository,
            string ancestorObjectId,
            string descendantObjectId,
            CancellationToken cancellationToken = default) => false;

        public void ApplyUpdates(
            GitRepositoryContext repository,
            IReadOnlyList<RemoteMirrorReferenceUpdate> updates,
            CancellationToken cancellationToken = default)
        {
            foreach (var update in updates)
            {
                if (update.TargetObjectId is null)
                {
                    LocalReferences.Remove(update.ReferenceName);
                }
                else
                {
                    LocalReferences[update.ReferenceName] = update.TargetObjectId;
                }
            }
        }

        public void DeleteNamespace(
            GitRepositoryContext repository,
            string referencePrefix,
            CancellationToken cancellationToken = default)
        {
        }
    }

    private sealed class FakeSyncBackend : IRemoteRepositorySyncBackend
    {
        public List<RemoteRepositorySyncRequest> Requests { get; } = [];

        public Task<RemoteRepositorySyncResult> ExecuteAsync(
            RemoteRepositorySyncRequest request,
            RemoteCredential credential,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new RemoteRepositorySyncResult(request.Operation, TimeSpan.Zero));
        }
    }

    private sealed class FakeRemoteProvider : IRemoteRepositoryProvider
    {
        public RemoteProviderKind Kind => RemoteProviderKind.GitHub;
        public Uri ServerUrl { get; } = new("https://github.com/");
        public RemoteProviderCapabilities Capabilities => RemoteProviderCapabilities.PullMirror | RemoteProviderCapabilities.PushMirror;
        public IReadOnlySet<RemoteAuthenticationKind> AuthenticationKinds { get; } =
            new HashSet<RemoteAuthenticationKind> { RemoteAuthenticationKind.PersonalAccessToken };

        public IReadOnlySet<string> GetRequiredScopes(
            RemoteAccountKind accountKind,
            RemoteRepositoryOperations operations) => new HashSet<string>(["repo"], StringComparer.Ordinal);

        public Task<RemoteProviderDiagnostic> TestAsync(RemoteAccountConnectionContext connection, RemoteCredential credential, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RemoteAccountProfile?> GetAccountAsync(RemoteAccountConnectionContext connection, RemoteCredential credential, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RemoteRepositoryPage> GetRepositoriesAsync(RemoteAccountConnectionContext connection, RemoteCredential credential, string? cursor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeProviderCatalog(IRemoteRepositoryProvider provider) : IRemoteProviderCatalog
    {
        public IReadOnlyList<RemoteProviderKind> AvailableProviders { get; } = [provider.Kind];
        public IRemoteRepositoryProvider? Get(RemoteProviderKind kind) => kind == provider.Kind ? provider : null;
    }

    private sealed class FakeCredentialVault : IRemoteCredentialVault
    {
        public Task<RemoteCredentialMetadata> StoreAsync(RemoteConnectionOwner owner, RemoteCredential credential, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<RemoteCredential?> ResolveAsync(RemoteSecretReference reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RemoteCredential?>(new RemoteCredential(
                RemoteAuthenticationKind.PersonalAccessToken,
                new RemoteSecret("fixture-secret"),
                ["repo"]));

        public Task<RemoteCredentialMetadata?> RotateAsync(RemoteSecretReference reference, RemoteCredential replacement, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RevokeAsync(RemoteSecretReference reference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoopPushEventSink : IRemoteMirrorPushEventSink
    {
        public Task EnqueueAsync(long repositoryId, IReadOnlyList<RemoteMirrorRefEvent> updates, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakePathResolver : IGitRepositoryPathResolver
    {
        public string RepositoryRootPath => Path.GetFullPath("repositories");
        public string ResolveRepositoryPath(string repositoryName) => Path.Combine(RepositoryRootPath, repositoryName);
    }
}
