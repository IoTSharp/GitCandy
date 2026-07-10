using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Algorithms;
using Microsoft.DevTunnels.Ssh.Tcp;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Ssh;

/// <summary>
/// 在 ASP.NET Core host 内运行现代 SSH listener 和 Git session handler。
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
    private readonly ConcurrentDictionary<SshServerSession, SessionRegistration> _sessions = new();
    private CancellationTokenSource? _serverStopping;
    private IReadOnlyList<IKeyPair>? _hostKeyPairs;
    private Task? _acceptSessionsTask;
    private TraceSource? _traceSource;
    private SshServer? _server;
    private bool _isRunning;

    /// <inheritdoc />
    public bool IsRunning => Volatile.Read(ref _isRunning);

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
            var hostKeyPairs = CreateHostKeyPairs(hostKeys);
            TraceSource? traceSource = null;
            SshServer? server = null;
            Task? acceptSessionsTask = null;
            try
            {
                traceSource = new TraceSource("GitCandy.Ssh.Protocol", SourceLevels.Warning);
                traceSource.Listeners.Clear();
                var listenerFactory = new DualModeTcpListenerFactory();
                server = new SshServer(SshProtocolStack.CreateServerConfiguration(), traceSource)
                {
                    Credentials = new SshServerCredentials(hostKeyPairs),
                    TcpListenerFactory = listenerFactory
                };

                _serverStopping = new CancellationTokenSource();
                server.SessionOpened += HandleSessionOpened;
                server.ExceptionRaised += HandleServerException;
                acceptSessionsTask = server.AcceptSessionsAsync(port, IPAddress.IPv6Any);
                await listenerFactory.Listening.WaitAsync(cancellationToken);
                if (acceptSessionsTask.IsCompleted)
                {
                    await acceptSessionsTask;
                    throw new InvalidOperationException("The built-in SSH listener stopped during startup.");
                }

                _server = server;
                _traceSource = traceSource;
                _hostKeyPairs = hostKeyPairs;
                _acceptSessionsTask = acceptSessionsTask;
                Volatile.Write(ref _isRunning, true);
                _ = MonitorAcceptSessionsAsync(acceptSessionsTask, _serverStopping.Token);
            }
            catch
            {
                if (server is not null)
                {
                    server.SessionOpened -= HandleSessionOpened;
                    server.ExceptionRaised -= HandleServerException;
                    server.Dispose();
                }

                if (acceptSessionsTask is not null)
                {
                    try
                    {
                        await acceptSessionsTask;
                    }
                    catch
                    {
                        // The startup exception being rethrown remains the actionable failure.
                    }
                }

                traceSource?.Close();
                DisposeHostKeys(hostKeyPairs);
                _serverStopping?.Dispose();
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
            Volatile.Write(ref _isRunning, false);
            _serverStopping?.Cancel();
            server.SessionOpened -= HandleSessionOpened;
            server.ExceptionRaised -= HandleServerException;
            try
            {
                server.Dispose();
                if (_acceptSessionsTask is not null)
                {
                    await _acceptSessionsTask.WaitAsync(cancellationToken);
                }
            }
            finally
            {
                foreach (var registration in _sessions.Values)
                {
                    registration.Dispose();
                }

                _sessions.Clear();
                _acceptSessionsTask = null;
                _traceSource?.Close();
                _traceSource = null;
                DisposeHostKeys(_hostKeyPairs);
                _hostKeyPairs = null;
                _serverStopping?.Dispose();
                _serverStopping = null;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void HandleSessionOpened(object? sender, SshServerSession session)
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
                session.Dispose();
                return;
            }

            session.Closed += HandleSessionClosed;
        }
        catch
        {
            scope.Dispose();
            session.Dispose();
            throw;
        }
    }

    private void HandleSessionClosed(object? sender, EventArgs args)
    {
        if (sender is not SshServerSession session
            || !_sessions.TryRemove(session, out var registration))
        {
            return;
        }

        session.Closed -= HandleSessionClosed;
        registration.Dispose();
    }

    private void HandleServerException(object? sender, Exception exception)
    {
        if (exception is Microsoft.DevTunnels.Ssh.SshConnectionException)
        {
            _logger.LogDebug(exception, "Built-in SSH connection ended with a protocol error.");
            return;
        }

        _logger.LogError(exception, "Built-in SSH connection failed unexpectedly.");
    }

    private async Task MonitorAcceptSessionsAsync(
        Task acceptSessionsTask,
        CancellationToken serverStoppingToken)
    {
        try
        {
            await acceptSessionsTask;
            if (!serverStoppingToken.IsCancellationRequested)
            {
                Volatile.Write(ref _isRunning, false);
                _logger.LogError("Built-in SSH listener stopped unexpectedly.");
            }
        }
        catch (Exception exception) when (serverStoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug(exception, "Built-in SSH listener stopped with the application host.");
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _isRunning, false);
            _logger.LogError(exception, "Built-in SSH listener failed unexpectedly.");
        }
    }

    private static IKeyPair[] CreateHostKeyPairs(IReadOnlyList<SshHostKey> hostKeys)
    {
        var hostKeyPairs = new List<IKeyPair>(hostKeys.Count);
        try
        {
            foreach (var hostKey in hostKeys)
            {
                hostKeyPairs.Add(CreateHostKeyPair(hostKey));
            }

            return hostKeyPairs.ToArray();
        }
        catch
        {
            DisposeHostKeys(hostKeyPairs);
            throw;
        }
    }

    private static IKeyPair CreateHostKeyPair(SshHostKey hostKey)
    {
        if (!string.Equals(hostKey.KeyType, "ssh-rsa", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported SSH host key type: {hostKey.KeyType}.");
        }

        var rsa = RSA.Create();
        try
        {
            rsa.FromXmlString(hostKey.PrivateKeyXml);
            return new Rsa.KeyPair(rsa);
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    private static void DisposeHostKeys(IReadOnlyList<IKeyPair>? hostKeys)
    {
        if (hostKeys is null)
        {
            return;
        }

        foreach (var hostKey in hostKeys)
        {
            hostKey.Dispose();
        }
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

    private sealed class DualModeTcpListenerFactory : ITcpListenerFactory
    {
        private readonly TaskCompletionSource<bool> _listening = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Listening => _listening.Task;

        public Task<TcpListener> CreateTcpListenerAsync(
            int? remotePort,
            IPAddress localIPAddress,
            int localPort,
            bool canChangeLocalPort,
            TraceSource trace,
            CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var listener = new TcpListener(localIPAddress, localPort);
                if (localIPAddress.Equals(IPAddress.IPv6Any))
                {
                    listener.Server.DualMode = true;
                }

                listener.ExclusiveAddressUse = true;
                listener.Start();
                _listening.TrySetResult(true);
                return Task.FromResult(listener);
            }
            catch (Exception exception)
            {
                _listening.TrySetException(exception);
                throw;
            }
        }
    }
}
