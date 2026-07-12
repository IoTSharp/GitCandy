using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Data.SonnetDB;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class GitCandySonnetDbSmokeTests
{
    [TestMethod]
    public async Task SonnetDB_WithMigrationsIdentityAndRepository_PersistsAndReadsData()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            $"sonnetdb-{Guid.NewGuid():N}");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sonnetdb",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath}"
                })
                .Build();
            var services = new ServiceCollection();

            services.AddLogging();
            services.AddGitCandyData(configuration, builder => builder.AddSonnetDB());
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
                    UserName = "sonnet-user",
                    Email = "sonnet-user@example.com"
                },
                "SonnetDB-Valid-Password-2026!");

            Assert.IsTrue(
                createResult.Succeeded,
                string.Join(Environment.NewLine, createResult.Errors.Select(static error => error.Description)));

            dbContext.Repositories.Add(new GitCandyRepository
            {
                NamespaceId = GitCandyNamespace.LegacyNamespaceId,
                StorageName = "sonnet-repository",
                Name = "SonnetRepository",
                NormalizedName = "SONNETREPOSITORY",
                Description = "SonnetDB provider smoke test",
                CreatedAtUtc = DateTime.UtcNow,
                IsPrivate = true
            });
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            var savedUser = await userManager.FindByNameAsync("sonnet-user");
            Assert.IsNotNull(savedUser);
            Assert.IsTrue(await userManager.CheckPasswordAsync(savedUser, "SonnetDB-Valid-Password-2026!"));
            Assert.IsTrue(await dbContext.Repositories.AnyAsync(repository =>
                repository.NormalizedName == "SONNETREPOSITORY"));
        }
        finally
        {
            if (Directory.Exists(databasePath))
            {
                Directory.Delete(databasePath, recursive: true);
            }
        }
    }
}
