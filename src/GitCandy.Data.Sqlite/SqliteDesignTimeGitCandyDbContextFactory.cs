using GitCandy.Data;
using GitCandy.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Sqlite;

/// <summary>
/// SQLite migrations 的设计时 DbContext 工厂。
/// </summary>
public sealed class SqliteDesignTimeGitCandyDbContextFactory : IDesignTimeDbContextFactory<GitCandyDbContext>
{
    /// <inheritdoc />
    public GitCandyDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault(static arg =>
                arg.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            ?? "Data Source=.data/gitcandy-design.db";

        var services = new ServiceCollection();
        var options = new GitCandyDatabaseOptions
        {
            Provider = GitCandyDatabaseProvider.Sqlite,
            ConnectionString = connectionString
        };

        services.AddDbContext<GitCandyDbContext>(dbContextOptions =>
        {
            dbContextOptions.UseSqlite(connectionString, sqliteOptions =>
                sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                    .MigrationsAssembly("GitCandy.Data.Sqlite"));
            dbContextOptions.ConfigureGitCandyCommonOptions(options);
        });

        return services.BuildServiceProvider(validateScopes: true)
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<GitCandyDbContext>();
    }
}
