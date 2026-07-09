using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Data.Permissions;
using GitCandy.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class GitCandyDataServiceCollectionExtensionsTests
{
    [TestMethod]
    public async Task AddGitCandyData_WithSqliteProvider_CreatesIdentityDatabase()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            $"{Guid.NewGuid():N}.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["GitCandy:Database:DbContextPoolSize"] = "16",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = serviceProvider.CreateAsyncScope();

            var options = scope.ServiceProvider.GetRequiredService<IOptions<GitCandyDatabaseOptions>>().Value;
            Assert.AreEqual(GitCandyDatabaseProvider.Sqlite, options.Provider);
            Assert.AreEqual(16, options.DbContextPoolSize);

            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.EnsureCreatedAsync();

            dbContext.Users.Add(new GitCandyUser
            {
                UserName = "admin",
                Email = "admin@example.com"
            });
            await dbContext.SaveChangesAsync();

            var user = await dbContext.Users.SingleAsync(item => item.UserName == "admin");

            Assert.AreEqual("admin@example.com", user.Email);
            Assert.IsTrue(File.Exists(databasePath));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [TestMethod]
    public async Task AddGitCandyData_WithDomainRoles_CreatesReadsWritesAndQueriesPermissions()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            $"{Guid.NewGuid():N}.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = serviceProvider.CreateAsyncScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.EnsureCreatedAsync();

            var alice = new GitCandyUser
            {
                Id = "user-alice",
                UserName = "alice",
                NormalizedUserName = "ALICE",
                Email = "alice@example.com",
                NormalizedEmail = "ALICE@EXAMPLE.COM"
            };
            var bob = new GitCandyUser
            {
                Id = "user-bob",
                UserName = "bob",
                NormalizedUserName = "BOB",
                Email = "bob@example.com",
                NormalizedEmail = "BOB@EXAMPLE.COM"
            };
            var carol = new GitCandyUser
            {
                Id = "user-carol",
                UserName = "carol",
                NormalizedUserName = "CAROL",
                Email = "carol@example.com",
                NormalizedEmail = "CAROL@EXAMPLE.COM"
            };

            var publicRepository = new GitCandyRepository
            {
                Name = "public-demo",
                Description = "Public demo repository",
                CreatedAtUtc = DateTime.UtcNow,
                AllowAnonymousRead = true
            };
            var privateRepository = new GitCandyRepository
            {
                Name = "private-demo",
                Description = "Private demo repository",
                CreatedAtUtc = DateTime.UtcNow,
                IsPrivate = true
            };
            var coreTeam = new GitCandyTeam
            {
                Name = "core",
                Description = "Core maintainers",
                CreatedAtUtc = DateTime.UtcNow
            };

            dbContext.Users.AddRange(alice, bob, carol);
            dbContext.Repositories.AddRange(publicRepository, privateRepository);
            dbContext.Teams.Add(coreTeam);
            await dbContext.SaveChangesAsync();

            dbContext.UserRepositoryRoles.Add(new GitCandyUserRepositoryRole
            {
                UserId = alice.Id,
                RepositoryId = privateRepository.Id,
                AllowRead = true,
                AllowWrite = true,
                IsOwner = true
            });
            dbContext.UserTeamRoles.Add(new GitCandyUserTeamRole
            {
                UserId = bob.Id,
                TeamId = coreTeam.Id
            });
            dbContext.TeamRepositoryRoles.Add(new GitCandyTeamRepositoryRole
            {
                TeamId = coreTeam.Id,
                RepositoryId = privateRepository.Id,
                AllowRead = true,
                AllowWrite = true
            });
            await dbContext.SaveChangesAsync();

            var savedRepository = await dbContext.Repositories
                .SingleAsync(repository => repository.Name == "private-demo");
            Assert.AreEqual("PRIVATE-DEMO", savedRepository.NormalizedName);

            var permissionQuery = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

            Assert.IsTrue(await permissionQuery.CanReadRepositoryAsync("PUBLIC-DEMO", null, isAdministrator: false));
            Assert.IsFalse(await permissionQuery.CanWriteRepositoryAsync("public-demo", null, isAdministrator: false));
            Assert.IsTrue(await permissionQuery.CanReadRepositoryAsync("private-demo", alice.Id, isAdministrator: false));
            Assert.IsTrue(await permissionQuery.CanWriteRepositoryAsync("private-demo", alice.Id, isAdministrator: false));
            Assert.IsTrue(await permissionQuery.CanReadRepositoryAsync("private-demo", bob.Id, isAdministrator: false));
            Assert.IsTrue(await permissionQuery.CanWriteRepositoryAsync("private-demo", bob.Id, isAdministrator: false));
            Assert.IsFalse(await permissionQuery.CanReadRepositoryAsync("private-demo", carol.Id, isAdministrator: false));
            Assert.IsTrue(await permissionQuery.CanReadRepositoryAsync("PRIVATE-DEMO", carol.Id, isAdministrator: true));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [TestMethod]
    public void AddGitCandyData_WithUnregisteredProvider_ThrowsNotSupportedException()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "pgsql",
                ["ConnectionStrings:GitCandy"] = "Host=localhost;Database=GitCandy;Username=postgres;Password=postgres"
            })
            .Build();

        var services = new ServiceCollection();

        Assert.ThrowsExactly<NotSupportedException>(
            () => services.AddGitCandyData(configuration, builder => builder.AddSqlite()));
    }

    [TestMethod]
    public void AddGitCandyData_WithUnsupportedProvider_ThrowsNotSupportedException()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataBase"] = "mysql",
                ["ConnectionStrings:GitCandy"] = "Server=localhost;Database=GitCandy"
            })
            .Build();

        var services = new ServiceCollection();

        Assert.ThrowsExactly<NotSupportedException>(
            () => services.AddGitCandyData(configuration, builder => builder.AddSqlite()));
    }
}
