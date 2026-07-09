using GitCandy.Data;
using GitCandy.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SonnetDB.EntityFrameworkCore.Extensions;

namespace GitCandy.Data.SonnetDB;

/// <summary>
/// SonnetDB provider 注册扩展。
/// </summary>
public static class GitCandySonnetDbDataBuilderExtensions
{
    private const string MigrationsAssembly = "GitCandy.Data.SonnetDB";

    /// <summary>
    /// 注册 SonnetDB provider。
    /// </summary>
    /// <param name="builder">数据库构建器。</param>
    /// <returns>数据库构建器。</returns>
    public static GitCandyDatabaseBuilder AddSonnetDB(this GitCandyDatabaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddProvider(GitCandyDatabaseProvider.SonnetDB, static (services, options) =>
        {
            services.AddDbContextPool<GitCandyDbContext>(dbContextOptions =>
            {
                dbContextOptions.UseSonnetDB(options.ConnectionString, sonnetOptions =>
                    sonnetOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                        .MigrationsAssembly(MigrationsAssembly));

                dbContextOptions.ConfigureGitCandyCommonOptions(options);
            }, options.DbContextPoolSize);
        });
    }
}
