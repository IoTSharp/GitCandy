using GitCandy.Data;
using GitCandy.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.PostgreSql;

/// <summary>
/// PostgreSQL provider 注册扩展。
/// </summary>
public static class GitCandyPostgreSqlDataBuilderExtensions
{
    private const string MigrationsAssembly = "GitCandy.Data.PostgreSql";

    /// <summary>
    /// 注册 PostgreSQL provider。
    /// </summary>
    /// <param name="builder">数据库构建器。</param>
    /// <returns>数据库构建器。</returns>
    public static GitCandyDatabaseBuilder AddPostgreSql(this GitCandyDatabaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddProvider(GitCandyDatabaseProvider.PostgreSql, static (services, options) =>
        {
            services.AddDbContextPool<GitCandyDbContext>(dbContextOptions =>
            {
                dbContextOptions.UseNpgsql(options.ConnectionString, npgsqlOptions =>
                    npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                        .MigrationsAssembly(MigrationsAssembly));

                dbContextOptions.ConfigureGitCandyCommonOptions(options);
            }, options.DbContextPoolSize);
        });
    }
}
