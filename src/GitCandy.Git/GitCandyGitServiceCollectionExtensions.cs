using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GitCandy.Issues;
using GitCandy.PullRequests;
using GitCandy.Releases;
using GitCandy.Search;

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
        services.TryAddSingleton<IManagedGitRepositoryService, LibGit2RepositoryService>();
        services.TryAddSingleton<IRepositoryBrowserService, RepositoryBrowserService>();
        services.TryAddScoped<IIssueTemplateService, IssueTemplateService>();
        services.TryAddScoped<IPullRequestGitRepository, PullRequestGitRepository>();
        services.TryAddSingleton<IGitLfsObjectStore, GitLfsObjectStore>();
        services.TryAddSingleton<IReleaseTagResolver, ReleaseTagResolver>();
        services.TryAddSingleton<IReleaseAssetStore, ReleaseAssetStore>();
        services.TryAddSingleton<IGitContentSearchService, GitContentSearchService>();
        services.TryAddScoped<GitCandy.Application.IRepositoryLifecycleService, RepositoryLifecycleService>();
        services.TryAddSingleton<IGitTransportBackend, GitProcessTransportBackend>();
        services.TryAddSingleton<IGitExecutableResolver, GitExecutableResolver>();
        services.TryAddSingleton<IGitReceiveHookLauncher, GitReceiveHookLauncher>();
        services.TryAddTransient<IGitReceiveHookRunner, GitReceiveHookRunner>();

        return services;
    }
}
