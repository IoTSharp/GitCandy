using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GitCandy.Operations;

/// <summary>
/// GitCandy liveness 和 readiness 健康检查注册入口。
/// </summary>
public static class GitCandyHealthCheckExtensions
{
    private const string ReadyTag = "ready";

    /// <summary>
    /// 注册数据库、存储路径、Git backend 和 SSH listener readiness 检查。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>同一个服务集合。</returns>
    public static IServiceCollection AddGitCandyHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<RepositoryPathHealthCheck>();
        services.TryAddSingleton<CachePathHealthCheck>();
        services.TryAddSingleton<GitBackendHealthCheck>();
        services.TryAddSingleton<SshListenerHealthCheck>();

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(
                "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(5))
            .AddCheck<RepositoryPathHealthCheck>(
                "repository-path",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(5))
            .AddCheck<CachePathHealthCheck>(
                "cache-path",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(5))
            .AddCheck<GitBackendHealthCheck>(
                "git-backend",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(5))
            .AddCheck<SshListenerHealthCheck>(
                "ssh-listener",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(5));

        return services;
    }

    /// <summary>
    /// 映射无需依赖检查的 liveness 和完整 readiness 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>同一个端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapGitCandyHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = static _ => false
        });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains(ReadyTag)
        });

        return endpoints;
    }
}
