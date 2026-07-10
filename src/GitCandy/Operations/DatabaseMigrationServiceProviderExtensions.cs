using GitCandy.Configuration;
using GitCandy.Data;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Operations;

/// <summary>
/// GitCandy 显式数据库迁移入口。
/// </summary>
public static class DatabaseMigrationServiceProviderExtensions
{
    /// <summary>
    /// 创建运行目录并将数据库迁移到当前版本。正常应用启动不会调用此方法。
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

        logger.LogInformation("Applying GitCandy database migrations.");
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
