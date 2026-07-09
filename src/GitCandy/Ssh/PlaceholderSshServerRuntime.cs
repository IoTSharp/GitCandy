using Microsoft.Extensions.Logging;

namespace GitCandy.Ssh;

/// <summary>
/// 迁移期 SSH server 占位运行时，保留 hosted service 生命周期接入点。
/// </summary>
public sealed class PlaceholderSshServerRuntime(
    ILogger<PlaceholderSshServerRuntime> logger) : ISshServerRuntime
{
    private readonly object _syncRoot = new();
    private readonly ILogger<PlaceholderSshServerRuntime> _logger = logger;
    private bool _started;
    private int _port;

    /// <inheritdoc />
    public Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (_started)
            {
                _logger.LogDebug("SSH server lifecycle placeholder is already active on port {SshPort}.", _port);
                return Task.CompletedTask;
            }

            _started = true;
            _port = port;
        }

        _logger.LogInformation(
            "SSH server lifecycle placeholder is active on port {SshPort}; protocol listener migration is pending.",
            port);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int port;
        lock (_syncRoot)
        {
            if (!_started)
            {
                return Task.CompletedTask;
            }

            port = _port;
            _started = false;
            _port = 0;
        }

        _logger.LogInformation("SSH server lifecycle placeholder stopped on port {SshPort}.", port);
        return Task.CompletedTask;
    }
}
