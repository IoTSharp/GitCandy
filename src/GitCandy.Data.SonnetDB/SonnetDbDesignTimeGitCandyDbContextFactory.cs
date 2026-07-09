using GitCandy.Data;
using GitCandy.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using SonnetDB.EntityFrameworkCore.Extensions;

namespace GitCandy.Data.SonnetDB;

/// <summary>
/// SonnetDB migrations 的设计时 DbContext 工厂。
/// </summary>
public sealed class SonnetDbDesignTimeGitCandyDbContextFactory : IDesignTimeDbContextFactory<GitCandyDbContext>
{
    /// <inheritdoc />
    public GitCandyDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.FirstOrDefault(static arg =>
                arg.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
            ?? "Data Source=.data/gitcandy-sonnetdb-design";

        var services = new ServiceCollection();
        var options = new GitCandyDatabaseOptions
        {
            Provider = GitCandyDatabaseProvider.SonnetDB,
            ConnectionString = connectionString
        };

        services.AddDbContext<GitCandyDbContext>(dbContextOptions =>
        {
            dbContextOptions.UseSonnetDB(connectionString, sonnetOptions =>
                sonnetOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                    .MigrationsAssembly("GitCandy.Data.SonnetDB"));
            dbContextOptions.ConfigureGitCandyCommonOptions(options);
        });

        return services.BuildServiceProvider(validateScopes: true)
            .CreateScope()
            .ServiceProvider
            .GetRequiredService<GitCandyDbContext>();
    }
}
