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
        await _serverRuntime.StartAsync(applicationOptions.SshPort, cancellationToken);
        _started = true;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return;
        }

        _logger.LogInformation("Stopping built-in SSH server lifecycle.");
        await _serverRuntime.StopAsync(cancellationToken);
        _started = false;
    }
}
