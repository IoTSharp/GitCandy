using GitCandy.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitCandy.Diagnostics;

/// <summary>
/// 记录 GitCandy ASP.NET Core host 启动和停止阶段的基础诊断信息。
/// </summary>
public sealed class GitCandyHostDiagnosticsHostedService(
    IWebHostEnvironment environment,
    IOptions<GitCandyApplicationOptions> applicationOptions,
    ILogger<GitCandyHostDiagnosticsHostedService> logger) : IHostedLifecycleService
{
    private readonly IWebHostEnvironment _environment = environment;
    private readonly IOptions<GitCandyApplicationOptions> _applicationOptions = applicationOptions;
    private readonly ILogger<GitCandyHostDiagnosticsHostedService> _logger = logger;

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        GitCandyApplicationOptions options;
        try
        {
            options = _applicationOptions.Value;
        }
        catch (OptionsValidationException ex)
        {
            _logger.LogError(
                ex,
                "GitCandy application configuration is invalid. Fix GitCandy:Application values before starting.");
            throw;
        }

        _logger.LogInformation(
            "GitCandy host is starting. Environment={EnvironmentName}; Application={ApplicationName}; ContentRoot={ContentRootPath}; WebRoot={WebRootPath}; SshEnabled={SshEnabled}; SshPort={SshPort}.",
            _environment.EnvironmentName,
            _environment.ApplicationName,
            _environment.ContentRootPath,
            string.IsNullOrWhiteSpace(_environment.WebRootPath) ? "(none)" : _environment.WebRootPath,
            options.EnableSsh,
            options.SshPort);

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
        _logger.LogInformation(
            "GitCandy host started. Environment={EnvironmentName}; Application={ApplicationName}.",
            _environment.EnvironmentName,
            _environment.ApplicationName);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "GitCandy host is stopping. Environment={EnvironmentName}; Application={ApplicationName}.",
            _environment.EnvironmentName,
            _environment.ApplicationName);

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
        _logger.LogInformation(
            "GitCandy host stopped. Environment={EnvironmentName}; Application={ApplicationName}.",
            _environment.EnvironmentName,
            _environment.ApplicationName);

        return Task.CompletedTask;
    }
}
