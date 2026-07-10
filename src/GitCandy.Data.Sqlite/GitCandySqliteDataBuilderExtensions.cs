using GitCandy.Data;
using GitCandy.Data.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Sqlite;

/// <summary>
/// SQLite provider 注册扩展。
/// </summary>
public static class GitCandySqliteDataBuilderExtensions
{
    private const string MigrationsAssembly = "GitCandy.Data.Sqlite";

    /// <summary>
    /// 注册 SQLite provider。
    /// </summary>
    /// <param name="builder">数据库构建器。</param>
    /// <returns>数据库构建器。</returns>
    public static GitCandyDatabaseBuilder AddSqlite(this GitCandyDatabaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddProvider(GitCandyDatabaseProvider.Sqlite, static (services, options) =>
        {
            EnsureSqliteDirectory(options.ConnectionString);

            services.AddPooledDbContextFactory<GitCandyDbContext>(dbContextOptions =>
            {
                dbContextOptions.UseSqlite(options.ConnectionString, sqliteOptions =>
                    sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)
                        .MigrationsAssembly(MigrationsAssembly));

                dbContextOptions.ConfigureGitCandyCommonOptions(options);
            }, options.DbContextPoolSize);
        });
    }

    private static void EnsureSqliteDirectory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(builder.DataSource)
            || string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var dataSource = builder.DataSource;
        if (dataSource.StartsWith("|DataDirectory|", StringComparison.OrdinalIgnoreCase))
        {
            dataSource = Path.Combine(
                AppContext.BaseDirectory,
                "App_Data",
                dataSource["|DataDirectory|".Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        var fullPath = Path.GetFullPath(dataSource, AppContext.BaseDirectory);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
