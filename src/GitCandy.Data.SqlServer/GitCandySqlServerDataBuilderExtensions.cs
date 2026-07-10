using GitCandy.Data;
using GitCandy.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.SqlServer;

/// <summary>
/// SQL Server provider 注册扩展。
/// </summary>
public static class GitCandySqlServerDataBuilderExtensions
{
    private const string MigrationsAssembly = "GitCandy.Data.SqlServer";

    /// <summary>
    /// 注册 SQL Server provider。
    /// </summary>
    /// <param name="builder">数据库构建器。</param>
    /// <returns>数据库构建器。</returns>
    public static GitCandyDatabaseBuilder AddSqlServer(this GitCandyDatabaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddProvider(GitCandyDatabaseProvider.SqlServer, static (services, options) =>
        {
            services.AddPooledDbContextFactory<GitCandyDbContext>(dbContextOptions =>
            {
                dbContextOptions.UseSqlServer(options.ConnectionString, sqlServerOptions =>
                    sqlServerOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                        .MigrationsAssembly(MigrationsAssembly));

                dbContextOptions.ConfigureGitCandyCommonOptions(options);
            }, options.DbContextPoolSize);
        });
    }
}
