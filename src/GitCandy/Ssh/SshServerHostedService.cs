using GitCandy.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitCandy.Ssh;

/// <summary>
/// 将内置 SSH server 接入 ASP.NET Core host 生命周期。
/// </summary>
public sealed class SshServerHostedService(
    IOptions<GitCandyApplicationOptions> options,
    ISshServerRuntime serverRuntime,
    ILogger<SshServerHostedService> logger) : IHostedService
{
    private readonly IOptions<GitCandyApplicationOptions> _options = options;
    private readonly ISshServerRuntime _serverRuntime = serverRuntime;
    private readonly ILogger<SshServerHostedService> _logger = logger;
    private bool _started;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var applicationOptions = _options.Value;
        if (!applicationOptions.EnableSsh)
        {
            _logger.LogInformation("Built-in SSH server is disabled by configuration.");
            return;
        }

        if (_started)
        {
            return;
        }

        _logger.LogInformation("Starting built-in SSH server lifecycle on port {SshPort}.", applicationOptions.SshPort);
        try
        {
            await _serverRuntime.StartAsync(applicationOptions.SshPort, cancellationToken);
            _started = true;
            _logger.LogInformation("Built-in SSH server lifecycle started on port {SshPort}.", applicationOptions.SshPort);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Built-in SSH server startup was canceled before port {SshPort} became active.",
                applicationOptions.SshPort);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to start built-in SSH server on port {SshPort}. Check whether the port is already in use and host key configuration is valid.",
                applicationOptions.SshPort);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return;
        }

        _logger.LogInformation("Stopping built-in SSH server lifecycle.");
        try
        {
            await _serverRuntime.StopAsync(cancellationToken);
            _started = false;
            _logger.LogInformation("Built-in SSH server lifecycle stopped.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Built-in SSH server shutdown was canceled before the runtime confirmed it had stopped.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop built-in SSH server cleanly.");
            throw;
        }
    }
}
