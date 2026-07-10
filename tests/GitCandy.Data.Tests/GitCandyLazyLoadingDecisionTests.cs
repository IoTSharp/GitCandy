using System.Reflection;
using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class GitCandyLazyLoadingDecisionTests
{
    private const string RepositoryName = "lazy-demo";
    private const string UserId = "user-lazy";

    private static readonly Type[] GitCandyEntityTypes =
    [
        typeof(GitCandyUser),
        typeof(GitCandyRepository),
        typeof(GitCandyTeam),
        typeof(GitCandyUserRepositoryRole),
        typeof(GitCandyTeamRepositoryRole),
        typeof(GitCandyUserTeamRole),
        typeof(GitCandySshKey)
    ];

    [TestMethod]
    public async Task AddGitCandyData_WithSqliteProvider_DoesNotConfigureLazyLoadingProxies()
    {
        await using var fixture = await LazyLoadingFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();

        Assert.IsFalse(dbContext.ChangeTracker.LazyLoadingEnabled);

        var extensionAssemblyNames = dbContext.GetService<IDbContextOptions>()
            .Extensions
            .Select(static extension => extension.GetType().Assembly.GetName().Name)
            .ToArray();

        CollectionAssert.DoesNotContain(
            extensionAssemblyNames,
            "Microsoft.EntityFrameworkCore.Proxies",
            "GitCandy must not register EF Core lazy-loading proxies.");
    }

    [TestMethod]
    public async Task DomainEntities_WithNavigationProperties_DoNotSupportProxyLazyLoading()
    {
        await using var fixture = await LazyLoadingFixture.CreateAsync();
        await using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var model = dbContext.Model;

        foreach (var entityType in model.GetEntityTypes()
            .Where(entityType => GitCandyEntityTypes.Contains(entityType.ClrType)))
        {
            Assert.IsTrue(
                entityType.ClrType.IsSealed,
                $"{entityType.ClrType.Name} must stay sealed so EF proxy lazy loading cannot be enabled accidentally.");

            AssertNoLazyLoaderConstructor(entityType.ClrType);

            foreach (var navigation in entityType.GetNavigations())
            {
                var propertyInfo = navigation.PropertyInfo
                    ?? throw new AssertFailedException($"{entityType.ClrType.Name}.{navigation.Name} must use a property navigation.");

                Assert.IsFalse(
                    IsOverridable(propertyInfo.GetMethod) || IsOverridable(propertyInfo.SetMethod),
                    $"{entityType.ClrType.Name}.{navigation.Name} must not be virtual; load it with Include, joins, or projections.");
            }
        }
    }

    [TestMethod]
    public async Task RepositoryNavigationAccess_WithoutInclude_DoesNotLoadRoles()
    {
        await using var fixture = await LazyLoadingFixture.CreateAsync();

        await using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            var repository = await dbContext.Repositories
                .SingleAsync(repository => repository.Name == RepositoryName);

            Assert.IsFalse(dbContext.Entry(repository).Collection(item => item.UserRoles).IsLoaded);
            Assert.AreEqual(0, repository.UserRoles.Count);
        }

        await using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            var repository = await dbContext.Repositories
                .Include(repository => repository.UserRoles)
                .SingleAsync(repository => repository.Name == RepositoryName);

            Assert.IsTrue(dbContext.Entry(repository).Collection(item => item.UserRoles).IsLoaded);
            Assert.AreEqual(1, repository.UserRoles.Count);
        }
    }

    private static void AssertNoLazyLoaderConstructor(Type entityClrType)
    {
        var hasLazyLoaderConstructorParameter = entityClrType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(static constructor => constructor.GetParameters())
            .Any(static parameter =>
                string.Equals(
                    parameter.ParameterType.FullName,
                    "Microsoft.EntityFrameworkCore.Infrastructure.ILazyLoader",
                    StringComparison.Ordinal)
                || parameter.ParameterType == typeof(Action<object, string>));

        Assert.IsFalse(
            hasLazyLoaderConstructorParameter,
            $"{entityClrType.Name} must not use EF Core ILazyLoader or lazy-loading delegates.");
    }

    private static bool IsOverridable(MethodInfo? method)
    {
        return method is { IsVirtual: true, IsFinal: false };
    }

    private sealed class LazyLoadingFixture : IAsyncDisposable
    {
        private LazyLoadingFixture(ServiceProvider serviceProvider, string databasePath)
        {
            ServiceProvider = serviceProvider;
            DatabasePath = databasePath;
        }

        private ServiceProvider ServiceProvider { get; }

        private string DatabasePath { get; }

        public static async Task<LazyLoadingFixture> CreateAsync()
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
            var fixture = new LazyLoadingFixture(serviceProvider, databasePath);

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

            await dbContext.Database.MigrateAsync();

            var repository = new GitCandyRepository
            {
                Name = RepositoryName,
                Description = "Lazy loading decision repository",
                CreatedAtUtc = DateTime.UtcNow,
                IsPrivate = true
            };

            dbContext.Users.Add(new GitCandyUser
            {
                Id = UserId,
                UserName = "lazy",
                NormalizedUserName = "LAZY",
                Email = "lazy@gitcandy.local",
                NormalizedEmail = "LAZY@GITCANDY.LOCAL"
            });
            dbContext.Repositories.Add(repository);
            await dbContext.SaveChangesAsync();

            dbContext.UserRepositoryRoles.Add(new GitCandyUserRepositoryRole
            {
                UserId = UserId,
                RepositoryId = repository.Id,
                AllowRead = true,
                AllowWrite = true,
                IsOwner = true
            });
            await dbContext.SaveChangesAsync();
        }
    }
}
