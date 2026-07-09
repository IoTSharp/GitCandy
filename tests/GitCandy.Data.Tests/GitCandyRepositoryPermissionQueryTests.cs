using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Data.Permissions;
using GitCandy.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class GitCandyRepositoryPermissionQueryTests
{
    private const string AdminUserId = "user-admin";
    private const string AliceUserId = "user-alice";
    private const string BobUserId = "user-bob";
    private const string CarolUserId = "user-carol";
    private const string PublicRepositoryName = "public-demo";
    private const string PrivateRepositoryName = "private-demo";

    [TestMethod]
    public async Task CanReadRepository_WithAnonymousAndPublicRepository_ReturnsTrue()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canRead = await query.CanReadRepositoryAsync(
            PublicRepositoryName.ToUpperInvariant(),
            userId: null,
            isAdministrator: false);

        Assert.IsTrue(canRead);
    }

    [TestMethod]
    public async Task CanWriteRepository_WithAnonymousAndPublicRepository_ReturnsFalse()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canWrite = await query.CanWriteRepositoryAsync(
            PublicRepositoryName,
            userId: null,
            isAdministrator: false);

        Assert.IsFalse(canWrite);
    }

    [TestMethod]
    public async Task CanReadRepository_WithAnonymousAndPrivateRepository_ReturnsFalse()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canRead = await query.CanReadRepositoryAsync(
            PrivateRepositoryName,
            userId: null,
            isAdministrator: false);

        Assert.IsFalse(canRead);
    }

    [TestMethod]
    public async Task CanReadRepository_WithPrivateRepositoryAndOwner_ReturnsTrue()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canRead = await query.CanReadRepositoryAsync(
            PrivateRepositoryName,
            AliceUserId,
            isAdministrator: false);

        Assert.IsTrue(canRead);
    }

    [TestMethod]
    public async Task CanWriteRepository_WithPrivateRepositoryAndOwner_ReturnsTrue()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canWrite = await query.CanWriteRepositoryAsync(
            PrivateRepositoryName,
            AliceUserId,
            isAdministrator: false);

        Assert.IsTrue(canWrite);
    }

    [TestMethod]
    public async Task CanReadRepository_WithPrivateRepositoryAndTeamMember_ReturnsTrue()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canRead = await query.CanReadRepositoryAsync(
            PrivateRepositoryName,
            BobUserId,
            isAdministrator: false);

        Assert.IsTrue(canRead);
    }

    [TestMethod]
    public async Task CanWriteRepository_WithPrivateRepositoryAndTeamMember_ReturnsTrue()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canWrite = await query.CanWriteRepositoryAsync(
            PrivateRepositoryName,
            BobUserId,
            isAdministrator: false);

        Assert.IsTrue(canWrite);
    }

    [TestMethod]
    public async Task CanReadRepository_WithPrivateRepositoryAndAdministrator_ReturnsTrue()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canRead = await query.CanReadRepositoryAsync(
            PrivateRepositoryName,
            AdminUserId,
            isAdministrator: true);

        Assert.IsTrue(canRead);
    }

    [TestMethod]
    public async Task CanWriteRepository_WithPrivateRepositoryAndAdministrator_ReturnsTrue()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canWrite = await query.CanWriteRepositoryAsync(
            PrivateRepositoryName,
            AdminUserId,
            isAdministrator: true);

        Assert.IsTrue(canWrite);
    }

    [TestMethod]
    public async Task CanReadRepository_WithPrivateRepositoryAndUnassignedUser_ReturnsFalse()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canRead = await query.CanReadRepositoryAsync(
            PrivateRepositoryName,
            CarolUserId,
            isAdministrator: false);

        Assert.IsFalse(canRead);
    }

    [TestMethod]
    public async Task CanWriteRepository_WithPrivateRepositoryAndUnassignedUser_ReturnsFalse()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canWrite = await query.CanWriteRepositoryAsync(
            PrivateRepositoryName,
            CarolUserId,
            isAdministrator: false);

        Assert.IsFalse(canWrite);
    }

    [TestMethod]
    public async Task CanReadRepository_WithMissingRepositoryAndAdministrator_ReturnsFalse()
    {
        await using var fixture = await PermissionQueryFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

        var canRead = await query.CanReadRepositoryAsync(
            "missing-demo",
            AdminUserId,
            isAdministrator: true);

        Assert.IsFalse(canRead);
    }

    private sealed class PermissionQueryFixture : IAsyncDisposable
    {
        private PermissionQueryFixture(ServiceProvider serviceProvider, string databasePath)
        {
            ServiceProvider = serviceProvider;
            DatabasePath = databasePath;
        }

        private ServiceProvider ServiceProvider { get; }

        private string DatabasePath { get; }

        public static async Task<PermissionQueryFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                "GitCandy.Tests",
                $"{Guid.NewGuid():N}.db");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var fixture = new PermissionQueryFixture(serviceProvider, databasePath);

            try
            {
                await fixture.SeedAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public AsyncServiceScope CreateScope()
        {
            return ServiceProvider.CreateAsyncScope();
        }

        public async ValueTask DisposeAsync()
        {
            await ServiceProvider.DisposeAsync();

            if (File.Exists(DatabasePath))
            {
                File.Delete(DatabasePath);
            }
        }

        private async Task SeedAsync()
        {
            await using var scope = CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();

            await dbContext.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var publicRepository = new GitCandyRepository
            {
                Name = PublicRepositoryName,
                Description = "Public sample repository",
                CreatedAtUtc = now,
                IsPrivate = false,
                AllowAnonymousRead = true,
                AllowAnonymousWrite = false
            };
            var privateRepository = new GitCandyRepository
            {
                Name = PrivateRepositoryName,
                Description = "Private sample repository",
                CreatedAtUtc = now,
                IsPrivate = true,
                AllowAnonymousRead = false,
                AllowAnonymousWrite = false
            };
            var coreTeam = new GitCandyTeam
            {
                Name = "core",
                Description = "Core maintainers",
                CreatedAtUtc = now
            };

            dbContext.Users.AddRange(
                NewUser(AdminUserId, "admin"),
                NewUser(AliceUserId, "alice"),
                NewUser(BobUserId, "bob"),
                NewUser(CarolUserId, "carol"));
            dbContext.Repositories.AddRange(publicRepository, privateRepository);
            dbContext.Teams.Add(coreTeam);
            await dbContext.SaveChangesAsync();

            dbContext.UserRepositoryRoles.AddRange(
                new GitCandyUserRepositoryRole
                {
                    UserId = AliceUserId,
                    RepositoryId = publicRepository.Id,
                    AllowRead = true,
                    AllowWrite = true,
                    IsOwner = true
                },
                new GitCandyUserRepositoryRole
                {
                    UserId = AliceUserId,
                    RepositoryId = privateRepository.Id,
                    AllowRead = true,
                    AllowWrite = true,
                    IsOwner = true
                });
            dbContext.UserTeamRoles.AddRange(
                new GitCandyUserTeamRole
                {
                    UserId = AliceUserId,
                    TeamId = coreTeam.Id,
                    IsAdministrator = true
                },
                new GitCandyUserTeamRole
                {
                    UserId = BobUserId,
                    TeamId = coreTeam.Id,
                    IsAdministrator = false
                });
            dbContext.TeamRepositoryRoles.Add(new GitCandyTeamRepositoryRole
            {
                TeamId = coreTeam.Id,
                RepositoryId = privateRepository.Id,
                AllowRead = true,
                AllowWrite = true
            });

            await dbContext.SaveChangesAsync();
        }

        private static GitCandyUser NewUser(string userId, string userName)
        {
            var normalizedUserName = GitCandyNameNormalizer.Normalize(userName);

            return new GitCandyUser
            {
                Id = userId,
                UserName = userName,
                NormalizedUserName = normalizedUserName,
                Email = $"{userName}@gitcandy.local",
                NormalizedEmail = $"{normalizedUserName}@GITCANDY.LOCAL"
            };
        }
    }
}
