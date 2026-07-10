using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GitCandy.Git;

/// <summary>
/// 注册 Git 仓库路径解析和受控 transport backend。
/// </summary>
public static class GitCandyGitServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Git transport 模块。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>同一个服务集合。</returns>
    public static IServiceCollection AddGitCandyGit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IGitRepositoryPathResolver, GitRepositoryPathResolver>();
        services.TryAddScoped<IGitServiceFactory, GitServiceFactory>();
        services.TryAddSingleton<IGitTransportBackend, GitProcessTransportBackend>();
        services.TryAddSingleton<IGitExecutableResolver, GitExecutableResolver>();

        return services;
    }
}
