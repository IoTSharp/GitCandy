using GitCandy.Data.Configuration;
using GitCandy.Data.Identity;
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
