using GitCandy.Application;
using GitCandy.Configuration;
using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class NameManagementServiceTests
{
    [TestMethod]
    public async Task RenameNamespaceAsync_WithThreeSuccessfulRenames_BlocksFourthAndResolvesAliases()
    {
        await using var fixture = await NameFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<INameManagementService>();
        var resolver = scope.ServiceProvider.GetRequiredService<IRepositoryAddressResolver>();

        var first = await service.RenameNamespaceAsync(fixture.NamespaceId, "alpha-two", fixture.UserId);
        var second = await service.RenameNamespaceAsync(fixture.NamespaceId, "alpha-three", fixture.UserId);
        var third = await service.RenameNamespaceAsync(fixture.NamespaceId, "alpha-four", fixture.UserId);
        var fourth = await service.RenameNamespaceAsync(fixture.NamespaceId, "alpha-five", fixture.UserId);

        Assert.AreEqual(NameChangeStatus.Succeeded, first.Status);
        Assert.AreEqual(NameChangeStatus.Succeeded, second.Status);
        Assert.AreEqual(NameChangeStatus.Succeeded, third.Status);
        Assert.AreEqual(0, third.RemainingRenames);
        Assert.AreEqual(NameChangeStatus.RateLimited, fourth.Status);
        var oldAddress = await resolver.ResolveAsync("alpha-one", "sample");
        Assert.IsNotNull(oldAddress);
        Assert.IsTrue(oldAddress.UsedNamespaceAlias);
        Assert.AreEqual("alpha-four", oldAddress.NamespaceSlug);
        Assert.AreEqual(fixture.RepositoryId, oldAddress.RepositoryId);
    }

    [TestMethod]
    public async Task RenameRepositoryAsync_WithAliasThenExpiry_ReleasesOldAddressIdempotently()
    {
        await using var fixture = await NameFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<INameManagementService>();
        var resolver = scope.ServiceProvider.GetRequiredService<IRepositoryAddressResolver>();

        var result = await service.RenameRepositoryAsync(fixture.RepositoryId, "renamed", fixture.UserId);
        Assert.AreEqual(NameChangeStatus.Succeeded, result.Status);
        var oldAddress = await resolver.ResolveAsync("alpha-one", "sample");
        Assert.IsNotNull(oldAddress);
        Assert.IsTrue(oldAddress.UsedRepositoryAlias);
        Assert.AreEqual("renamed", oldAddress.RepositorySlug);

        var released = await service.ReleaseExpiredAliasesAsync(fixture.UtcNow.AddDays(2));
        var releasedAgain = await service.ReleaseExpiredAliasesAsync(fixture.UtcNow.AddDays(2));
        Assert.AreEqual(1, released);
        Assert.AreEqual(0, releasedAgain);
        Assert.IsNull(await resolver.ResolveAsync("alpha-one", "sample"));
        Assert.IsNotNull(await resolver.ResolveAsync("alpha-one", "renamed"));
    }

    [TestMethod]
    public async Task RenameNamespaceAsync_WithReservedOrOccupiedSlug_DoesNotConsumeQuota()
    {
        await using var fixture = await NameFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<INameManagementService>();

        var reserved = await service.RenameNamespaceAsync(fixture.NamespaceId, "account", fixture.UserId);
        var occupied = await service.RenameNamespaceAsync(fixture.NamespaceId, "other-owner", fixture.UserId);
        var snapshot = await service.GetNamespaceSnapshotAsync("alpha-one");

        Assert.AreEqual(NameChangeStatus.Reserved, reserved.Status);
        Assert.AreEqual(NameChangeStatus.Occupied, occupied.Status);
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(3, snapshot.RemainingRenames);
    }

    [TestMethod]
    public async Task RenameNamespaceAsync_WithConcurrentFinalQuotaRequests_AllowsOnlyOneSuccess()
    {
        await using var fixture = await ConcurrentNameFixture.CreateAsync();
        await using (var preparationScope = fixture.Services.CreateAsyncScope())
        {
            var service = preparationScope.ServiceProvider.GetRequiredService<INameManagementService>();
            Assert.AreEqual(
                NameChangeStatus.Succeeded,
                (await service.RenameNamespaceAsync(fixture.NamespaceId, "parallel-two", fixture.UserId)).Status);
            Assert.AreEqual(
                NameChangeStatus.Succeeded,
                (await service.RenameNamespaceAsync(fixture.NamespaceId, "parallel-three", fixture.UserId)).Status);
        }

        async Task<NameChangeResult> RenameAsync(string slug)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<INameManagementService>();
            return await service.RenameNamespaceAsync(fixture.NamespaceId, slug, fixture.UserId);
        }

        var results = await Task.WhenAll(
            RenameAsync("parallel-four"),
            RenameAsync("parallel-five"));
        Assert.AreEqual(1, results.Count(result => result.Status == NameChangeStatus.Succeeded));

        await using var verificationScope = fixture.Services.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        Assert.AreEqual(3, await dbContext.RenameEvents.CountAsync(
            item => item.SubjectType == NameSubjectType.Namespace
                && item.EventType == NameEventType.Renamed));
    }

    private sealed class NameFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private NameFixture(
            ServiceProvider services,
            SqliteConnection connection,
            string userId,
            long namespaceId,
            long repositoryId,
            DateTime utcNow)
        {
            Services = services;
            _connection = connection;
            UserId = userId;
            NamespaceId = namespaceId;
            RepositoryId = repositoryId;
            UtcNow = utcNow;
        }

        public ServiceProvider Services { get; }
        public string UserId { get; }
        public long NamespaceId { get; }
        public long RepositoryId { get; }
        public DateTime UtcNow { get; }

        public static async Task<NameFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var utcNow = new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc);
            var services = new ServiceCollection();
            services.AddDbContext<GitCandyDbContext>(options => options.UseSqlite(connection));
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(utcNow));
            services.AddOptions<GitCandyNamespaceOptions>().Configure(options =>
            {
                options.AliasRetentionDays = 1;
                options.RenameLimit = 3;
                options.RenameWindowDays = 7;
            });
            services.AddGitCandyApplicationServices();
            var provider = services.BuildServiceProvider(validateScopes: true);

            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            var user = new GitCandyUser
            {
                Id = "alpha-user",
                UserName = "alpha-one",
                NormalizedUserName = "ALPHA-ONE",
                Email = "alpha@example.com",
                NormalizedEmail = "ALPHA@EXAMPLE.COM",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            dbContext.Users.Add(user);
            var namespaceItem = new GitCandyNamespace
            {
                OwnerType = NamespaceOwnerType.User,
                UserId = user.Id,
                Slug = user.UserName,
                CreatedAtUtc = utcNow
            };
            dbContext.Namespaces.Add(namespaceItem);
            await dbContext.SaveChangesAsync();
            dbContext.NamespaceClaims.Add(new GitCandyNamespaceClaim
            {
                NormalizedSlug = "ALPHA-ONE",
                Slug = "alpha-one",
                ClaimType = NameClaimType.Current,
                NamespaceId = namespaceItem.Id
            });
            var otherNamespace = new GitCandyNamespace
            {
                OwnerType = NamespaceOwnerType.System,
                Slug = "other-owner",
                CreatedAtUtc = utcNow
            };
            dbContext.Namespaces.Add(otherNamespace);
            await dbContext.SaveChangesAsync();
            dbContext.NamespaceClaims.Add(new GitCandyNamespaceClaim
            {
                NormalizedSlug = "OTHER-OWNER",
                Slug = "other-owner",
                ClaimType = NameClaimType.Current,
                NamespaceId = otherNamespace.Id
            });
            var repository = new GitCandyRepository
            {
                NamespaceId = namespaceItem.Id,
                Name = "sample",
                StorageName = "stable-storage",
                Description = "M10 fixture",
                CreatedAtUtc = utcNow,
                AllowAnonymousRead = true
            };
            dbContext.Repositories.Add(repository);
            await dbContext.SaveChangesAsync();
            dbContext.RepositoryClaims.Add(new GitCandyRepositoryClaim
            {
                NamespaceId = namespaceItem.Id,
                NormalizedSlug = "SAMPLE",
                Slug = "sample",
                ClaimType = NameClaimType.Current,
                RepositoryId = repository.Id
            });
            dbContext.LegacyRepositoryRoutes.Add(new GitCandyLegacyRepositoryRoute
            {
                Project = "sample",
                NormalizedProject = "SAMPLE",
                RepositoryId = repository.Id,
                CreatedAtUtc = utcNow
            });
            await dbContext.SaveChangesAsync();
            return new NameFixture(provider, connection, user.Id, namespaceItem.Id, repository.Id, utcNow);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(utcNow);
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class ConcurrentNameFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private ConcurrentNameFixture(
            ServiceProvider services,
            string databasePath,
            string userId,
            long namespaceId)
        {
            Services = services;
            _databasePath = databasePath;
            UserId = userId;
            NamespaceId = namespaceId;
        }

        public ServiceProvider Services { get; }
        public string UserId { get; }
        public long NamespaceId { get; }

        public static async Task<ConcurrentNameFixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), "GitCandy.Tests", $"{Guid.NewGuid():N}.db");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            var utcNow = new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc);
            var services = new ServiceCollection();
            services.AddDbContext<GitCandyDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=30"));
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(utcNow));
            services.AddOptions<GitCandyNamespaceOptions>().Configure(options =>
            {
                options.AliasRetentionDays = 365;
                options.RenameLimit = 3;
                options.RenameWindowDays = 7;
            });
            services.AddGitCandyApplicationServices();
            var provider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            var user = new GitCandyUser
            {
                Id = "parallel-user",
                UserName = "parallel-one",
                NormalizedUserName = "PARALLEL-ONE",
                Email = "parallel@example.com",
                NormalizedEmail = "PARALLEL@EXAMPLE.COM",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            dbContext.Users.Add(user);
            var namespaceItem = new GitCandyNamespace
            {
                OwnerType = NamespaceOwnerType.User,
                UserId = user.Id,
                Slug = user.UserName,
                CreatedAtUtc = utcNow
            };
            dbContext.Namespaces.Add(namespaceItem);
            await dbContext.SaveChangesAsync();
            dbContext.NamespaceClaims.Add(new GitCandyNamespaceClaim
            {
                NormalizedSlug = "PARALLEL-ONE",
                Slug = "parallel-one",
                ClaimType = NameClaimType.Current,
                NamespaceId = namespaceItem.Id
            });
            await dbContext.SaveChangesAsync();
            return new ConcurrentNameFixture(provider, databasePath, user.Id, namespaceItem.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
    }
}
