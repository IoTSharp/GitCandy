using GitCandy.Data.Configuration;
using GitCandy.Data.Identity;
using GitCandy.Data.Sqlite;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class GitCandyIdentityStoreSmokeTests
{
    [TestMethod]
    public async Task IdentityStore_WithNewUser_PersistsAndValidatesPassword()
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

            services.AddLogging();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());
            services.AddIdentityCore<GitCandyUser>()
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<GitCandyDbContext>();

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.MigrateAsync();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
            var createResult = await userManager.CreateAsync(
                new GitCandyUser
                {
                    UserName = "m3-user",
                    Email = "m3-user@example.com"
                },
                "M3-Valid-Password-2026!");

            Assert.IsTrue(
                createResult.Succeeded,
                string.Join(Environment.NewLine, createResult.Errors.Select(static error => error.Description)));

            var savedUser = await userManager.FindByNameAsync("m3-user");
            Assert.IsNotNull(savedUser);
            Assert.IsTrue(await userManager.CheckPasswordAsync(savedUser, "M3-Valid-Password-2026!"));
            Assert.IsFalse(await userManager.CheckPasswordAsync(savedUser, "incorrect-password"));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
