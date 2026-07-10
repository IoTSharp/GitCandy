using GitCandy.Data.Configuration;
using GitCandy.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class GitCandyDbContextLifetimeTests
{
    [TestMethod]
    public async Task AddGitCandyData_WithScopedServices_ReusesContextOnlyInsideScope()
    {
        var configuration = CreateSqliteConfiguration();
        var services = new ServiceCollection();
        services.AddGitCandyData(configuration, builder => builder.AddSqlite());

        await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        await using var firstScope = serviceProvider.CreateAsyncScope();
        await using var secondScope = serviceProvider.CreateAsyncScope();

        var firstContext = firstScope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var repeatedContext = firstScope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<GitCandyDbContext>();

        Assert.AreSame(firstContext, repeatedContext);
        Assert.AreNotSame(firstContext, secondContext);
    }

    [TestMethod]
    public async Task DbContextFactory_WithConcurrentBackgroundWork_CreatesIndependentContexts()
    {
        var configuration = CreateSqliteConfiguration();
        var services = new ServiceCollection();
        services.AddGitCandyData(configuration, builder => builder.AddSqlite());

        await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<GitCandyDbContext>>();

        var firstContextTask = factory.CreateDbContextAsync();
        var secondContextTask = factory.CreateDbContextAsync();
        await Task.WhenAll(firstContextTask, secondContextTask);

        await using var firstContext = await firstContextTask;
        await using var secondContext = await secondContextTask;

        Assert.AreNotEqual(firstContext.ContextId.InstanceId, secondContext.ContextId.InstanceId);
    }

    private static IConfiguration CreateSqliteConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "sqlite",
                ["GitCandy:Database:DbContextPoolSize"] = "8",
                ["ConnectionStrings:GitCandy"] = "Data Source=:memory:"
            })
            .Build();
    }
}
