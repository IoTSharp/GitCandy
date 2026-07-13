using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Data.SonnetDB;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GitCandy.Workspace;

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
            var sonnetUser = new GitCandyUser
            {
                UserName = "sonnet-user",
                Email = "sonnet-user@example.com"
            };
            var createResult = await userManager.CreateAsync(
                sonnetUser,
                "SonnetDB-Valid-Password-2026!");

            Assert.IsTrue(
                createResult.Succeeded,
                string.Join(Environment.NewLine, createResult.Errors.Select(static error => error.Description)));

            var repository = new GitCandyRepository
            {
                NamespaceId = GitCandyNamespace.LegacyNamespaceId,
                StorageName = "sonnet-repository",
                Name = "SonnetRepository",
                NormalizedName = "SONNETREPOSITORY",
                Description = "SonnetDB provider smoke test",
                CreatedAtUtc = DateTime.UtcNow,
                IsPrivate = true
            };
            dbContext.Repositories.Add(repository);
            await dbContext.SaveChangesAsync();
            dbContext.RepositoryStars.Add(new GitCandyRepositoryStar
            {
                RepositoryId = repository.Id,
                UserId = sonnetUser.Id,
                CreatedAtUtc = DateTime.UtcNow
            });
            dbContext.Todos.Add(new GitCandyTodo
            {
                UserId = sonnetUser.Id,
                RepositoryId = repository.Id,
                ResourceType = WorkspaceResourceType.Repository,
                ResourceId = $"repository:{repository.Id}",
                Kind = WorkspaceTodoKind.RepositoryRequest,
                Status = WorkspaceTodoStatus.Pending,
                Title = "SonnetDB workspace smoke",
                Url = "/legacy/SonnetRepository",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            var savedUser = await userManager.FindByNameAsync("sonnet-user");
            Assert.IsNotNull(savedUser);
            Assert.IsTrue(await userManager.CheckPasswordAsync(savedUser, "SonnetDB-Valid-Password-2026!"));
            Assert.IsTrue(await dbContext.Repositories.AnyAsync(repository =>
                repository.NormalizedName == "SONNETREPOSITORY"));
            Assert.AreEqual(1, await dbContext.RepositoryStars.CountAsync());
            Assert.AreEqual(1, await dbContext.Todos.CountAsync());
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
