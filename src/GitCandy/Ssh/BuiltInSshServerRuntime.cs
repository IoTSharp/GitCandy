using System.Collections.Concurrent;
using System.Net;
using GitCandy.Ssh;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Ssh;

/// <summary>
/// 在 ASP.NET Core host 内运行旧 GitCandy SSH listener 和 Git session handler。
/// </summary>
public sealed class BuiltInSshServerRuntime(
    ISshHostKeyProvider hostKeyProvider,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<BuiltInSshServerRuntime> logger) : ISshServerRuntime
{
    private readonly ISshHostKeyProvider _hostKeyProvider = hostKeyProvider;
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ILogger<BuiltInSshServerRuntime> _logger = logger;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ConcurrentDictionary<Session, SessionRegistration> _sessions = new();
    private CancellationTokenSource? _serverStopping;
    private SshServer? _server;

    /// <inheritdoc />
    public async Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_server is not null)
            {
                return;
            }

            var hostKeys = await _hostKeyProvider.GetHostKeysAsync(cancellationToken);
            var server = new SshServer(new StartingInfo(IPAddress.IPv6Any, port));
            foreach (var hostKey in hostKeys)
            {
                server.AddHostKey(hostKey.KeyType, hostKey.PrivateKeyXml);
            }

            _serverStopping = new CancellationTokenSource();
            server.ConnectionAccepted += HandleConnectionAccepted;
            server.ExceptionRasied += HandleServerException;
            try
            {
                server.Start();
                _server = server;
            }
            catch
            {
                server.ConnectionAccepted -= HandleConnectionAccepted;
                server.ExceptionRasied -= HandleServerException;
                server.Dispose();
                _serverStopping.Dispose();
                _serverStopping = null;
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            var server = _server;
            if (server is null)
            {
                return;
            }

            _server = null;
            _serverStopping?.Cancel();
            server.ConnectionAccepted -= HandleConnectionAccepted;
            server.ExceptionRasied -= HandleServerException;
            server.Stop();

            foreach (var registration in _sessions.Values)
            {
                registration.Dispose();
            }

            _sessions.Clear();
            _serverStopping?.Dispose();
            _serverStopping = null;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void HandleConnectionAccepted(object? sender, Session session)
    {
        var stoppingToken = _serverStopping?.Token ?? CancellationToken.None;
        var scope = _serviceScopeFactory.CreateScope();
        try
        {
            var handler = ActivatorUtilities.CreateInstance<GitSshSession>(
                scope.ServiceProvider,
                session,
                stoppingToken);
            var registration = new SessionRegistration(handler, scope);
            if (!_sessions.TryAdd(session, registration))
            {
                registration.Dispose();
                session.Disconnect();
                return;
            }

            session.Disconnected += HandleSessionDisconnected;
        }
        catch
        {
            scope.Dispose();
            session.Disconnect();
            throw;
        }
    }

    private void HandleSessionDisconnected(object? sender, EventArgs args)
    {
        if (sender is not Session session || !_sessions.TryRemove(session, out var registration))
        {
            return;
        }

        session.Disconnected -= HandleSessionDisconnected;
        registration.Dispose();
    }

    private void HandleServerException(object? sender, Exception exception)
    {
        if (exception is SshConnectionException)
        {
            _logger.LogDebug(exception, "Built-in SSH connection ended with a protocol error.");
            return;
        }

        _logger.LogError(exception, "Built-in SSH connection failed unexpectedly.");
    }

    private sealed class SessionRegistration(GitSshSession handler, IServiceScope scope) : IDisposable
    {
        private readonly GitSshSession _handler = handler;
        private readonly IServiceScope _scope = scope;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _handler.Dispose();
            _scope.Dispose();
        }
    }
}
