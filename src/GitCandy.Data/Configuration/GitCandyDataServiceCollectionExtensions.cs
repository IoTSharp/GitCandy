using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitCandy.Data.Configuration;

/// <summary>
/// GitCandy 数据层依赖注入扩展。
/// </summary>
public static class GitCandyDataServiceCollectionExtensions
{
    /// <summary>
    /// 按配置注册 GitCandy EF Core 数据层。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <param name="configureProviders">注册可用 provider 的回调。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddGitCandyData(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<GitCandyDatabaseBuilder> configureProviders)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configureProviders);

        var options = GitCandyDatabaseOptionsReader.Read(configuration);
        services.AddSingleton<IOptions<GitCandyDatabaseOptions>>(Options.Create(options));

        var builder = new GitCandyDatabaseBuilder(services, configuration);
        configureProviders(builder);

        if (!builder.TryConfigure(options))
        {
            throw new NotSupportedException(
                $"GitCandy database provider '{options.Provider}' is not registered.");
        }

        return services;
    }

    /// <summary>
    /// 应用跨 provider 的 EF Core 调试选项。
    /// </summary>
    /// <param name="builder">DbContext 选项构建器。</param>
    /// <param name="options">数据库配置。</param>
    public static void ConfigureGitCandyCommonOptions(
        this DbContextOptionsBuilder builder,
        GitCandyDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        if (options.EnableSensitiveDataLogging)
        {
            builder.EnableSensitiveDataLogging();
        }
    }
}
