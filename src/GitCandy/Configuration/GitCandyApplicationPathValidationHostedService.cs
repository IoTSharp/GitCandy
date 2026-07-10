using Microsoft.Extensions.Hosting;

namespace GitCandy.Configuration;

/// <summary>
/// 在 ASP.NET Core host 启动时验证 GitCandy 应用路径配置。
/// </summary>
internal sealed class GitCandyApplicationPathValidationHostedService(
    IGitCandyApplicationPaths applicationPaths) : IHostedLifecycleService
{
    private readonly IGitCandyApplicationPaths _applicationPaths = applicationPaths;

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _ = _applicationPaths.ContentRootPath;
        _ = _applicationPaths.WebRootPath;
        _ = _applicationPaths.LogPathFormat;
        _ = _applicationPaths.UserConfigurationPath;
        _ = _applicationPaths.RepositoryPath;
        _ = _applicationPaths.CachePath;
        _ = _applicationPaths.GitCorePath;
        _ = _applicationPaths.SshHostKeyPath;
        _ = _applicationPaths.DataProtectionKeysPath;
        _ = _applicationPaths.ResolvePathWithinRepositoryRoot(".");
        _ = _applicationPaths.ResolvePathWithinCacheRoot(".");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
