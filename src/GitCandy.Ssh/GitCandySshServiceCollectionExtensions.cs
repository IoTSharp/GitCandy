using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GitCandy.Ssh;

/// <summary>
/// 注册内置 SSH Git transport 及其宿主生命周期。
/// </summary>
public static class GitCandySshServiceCollectionExtensions
{
    /// <summary>
    /// 注册 SSH host key、访问校验、server runtime 和 hosted service。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>同一个服务集合。</returns>
    public static IServiceCollection AddGitCandySsh(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISshHostKeyProvider, FileSshHostKeyProvider>();
        services.TryAddSingleton<ISshServerRuntime, BuiltInSshServerRuntime>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, SshServerHostedService>());

        return services;
    }
}
