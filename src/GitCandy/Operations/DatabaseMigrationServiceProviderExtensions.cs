using GitCandy.Configuration;
using GitCandy.Data;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Operations;

/// <summary>
/// GitCandy 数据库迁移入口。
/// </summary>
public static class DatabaseMigrationServiceProviderExtensions
{
    /// <summary>
    /// 创建运行目录，检测待应用 migration，并在需要时将数据库迁移到当前版本。
    /// </summary>
    /// <param name="services">应用服务提供程序。</param>
    /// <param name="cancellationToken">迁移取消令牌。</param>
    public static async Task MigrateGitCandyDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;
        var paths = serviceProvider.GetRequiredService<IGitCandyApplicationPaths>();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("GitCandy.DatabaseMigration");

        Directory.CreateDirectory(paths.RepositoryPath);
        Directory.CreateDirectory(paths.CachePath);
        CreateParentDirectory(paths.LogPathFormat);
        CreateParentDirectory(paths.UserConfigurationPath);
        CreateParentDirectory(paths.SshHostKeyPath);
        Directory.CreateDirectory(paths.DataProtectionKeysPath);

        var dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<GitCandyDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var pendingMigrations = (await dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken))
            .ToArray();
        if (pendingMigrations.Length == 0)
        {
            logger.LogInformation("GitCandy database schema is up to date.");
            return;
        }

        logger.LogInformation(
            "Applying {MigrationCount} GitCandy database migrations.",
            pendingMigrations.Length);
        await dbContext.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("GitCandy database migrations completed.");
    }

    private static void CreateParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
