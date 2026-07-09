using GitCandy.Data;
using GitCandy.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.PostgreSql;

/// <summary>
/// PostgreSQL migrations 的设计时 DbContext 工厂。
/// </summary>
public sealed class PostgreSqlDesignTimeGitCandyDbContextFactory : IDesignTimeDbContextFactory<GitCandyDbContext>
{
    /// <inheritdoc />
    public GitCandyDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault(static arg =>
                arg.Contains("Host=", StringComparison.OrdinalIgnoreCase)
                || arg.Contains("Server=", StringComparison.OrdinalIgnoreCase))
            ?? "Host=localhost;Database=GitCandyDesign;Username=postgres;Password=postgres;Include Error Detail=true";

        var services = new ServiceCollection();
        var options = new GitCandyDatabaseOptions
        {
            Provider = GitCandyDatabaseProvider.PostgreSql,
            ConnectionString = connectionString
        };

        services.AddDbContext<GitCandyDbContext>(dbContextOptions =>
        {
            dbContextOptions.UseNpgsql(connectionString, npgsqlOptions =>
                npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                    .MigrationsAssembly("GitCandy.Data.PostgreSql"));
            dbContextOptions.ConfigureGitCandyCommonOptions(options);
        });

        return services.BuildServiceProvider(validateScopes: true)
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<GitCandyDbContext>();
    }
}
