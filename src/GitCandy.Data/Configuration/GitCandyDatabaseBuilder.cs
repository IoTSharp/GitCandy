using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Configuration;

/// <summary>
/// 数据库 provider 注册构建器。
/// </summary>
public sealed class GitCandyDatabaseBuilder
{
    private readonly Dictionary<GitCandyDatabaseProvider, Action<IServiceCollection, GitCandyDatabaseOptions>> _providers = [];

    internal GitCandyDatabaseBuilder(IServiceCollection services, IConfiguration configuration)
    {
        Services = services;
        Configuration = configuration;
    }

    /// <summary>
    /// 当前服务集合。
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// 当前应用配置。
    /// </summary>
    public IConfiguration Configuration { get; }

    /// <summary>
    /// 注册一个数据库 provider。
    /// </summary>
    /// <param name="provider">provider 类型。</param>
    /// <param name="configure">provider 的 DbContext 注册逻辑。</param>
    /// <returns>当前构建器。</returns>
    public GitCandyDatabaseBuilder AddProvider(
        GitCandyDatabaseProvider provider,
        Action<IServiceCollection, GitCandyDatabaseOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _providers[provider] = configure;
        return this;
    }

    internal bool TryConfigure(GitCandyDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!_providers.TryGetValue(options.Provider, out var configure))
        {
            return false;
        }

        configure(Services, options);
        return true;
    }
}
