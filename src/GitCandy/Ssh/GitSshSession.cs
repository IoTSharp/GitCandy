using System.IO.Pipelines;
using System.Text.RegularExpressions;
using GitCandy.Git;
using GitCandy.Ssh.Services;

namespace GitCandy.Ssh;

internal sealed class GitSshSession : IDisposable
{
    private static readonly Regex GitCommandPattern = new(
        "^(?<command>git-upload-pack|git-receive-pack|git-upload-archive) '/?git/(?<repository>[^/\\\\'\\r\\n]+)\\.git'$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private readonly Session _session;
    private readonly ISshAccessService _accessService;
    private readonly IGitRepositoryPathResolver _pathResolver;
    private readonly IGitTransportBackend _transportBackend;
    private readonly ILogger<GitSshSession> _logger;
    private readonly CancellationTokenSource _sessionCancellation;
    private readonly object _syncRoot = new();
    private UserauthService? _userauthService;
    private ConnectionService? _connectionService;
    private SessionChannel? _channel;
    private Pipe? _inputPipe;
    private Task? _transportTask;
    private SshPrincipal? _principal;
    private bool _commandStarted;
    private bool _inputCompleted;
    private bool _disposed;

    public GitSshSession(
        Session session,
        CancellationToken serverStoppingToken,
        ISshAccessService accessService,
        IGitRepositoryPathResolver pathResolver,
        IGitTransportBackend transportBackend,
        ILogger<GitSshSession> logger)
    {
        _session = session;
        _accessService = accessService;
        _pathResolver = pathResolver;
        _transportBackend = transportBackend;
        _logger = logger;
        _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(serverStoppingToken);
        _session.ServiceRegistered += HandleServiceRegistered;
    }

    public void Dispose()
    {
        Task? transportTask;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            transportTask = _transportTask;
        }

        _session.ServiceRegistered -= HandleServiceRegistered;
        if (_userauthService is not null)
        {
            _userauthService.Userauth -= HandleUserauth;
        }

        if (_connectionService is not null)
        {
            _connectionService.CommandOpened -= HandleCommandOpened;
        }

        CompleteInput();
        _sessionCancellation.Cancel();

        if (transportTask is null || transportTask.IsCompleted)
        {
            _sessionCancellation.Dispose();
        }
        else
        {
            _ = transportTask.ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                _sessionCancellation,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void HandleServiceRegistered(object? sender, SshService service)
    {
        if (service is UserauthService userauthService)
        {
            _userauthService = userauthService;
            userauthService.Userauth += HandleUserauth;
        }
        else if (service is ConnectionService connectionService)
        {
            _connectionService = connectionService;
            connectionService.CommandOpened += HandleCommandOpened;
        }
    }

    private void HandleUserauth(object? sender, UserauthArgs args)
    {
        try
        {
            _principal = _accessService.AuthenticateAsync(
                    args.KeyAlgorithm,
                    args.Key,
                    _sessionCancellation.Token)
                .GetAwaiter()
                .GetResult();
            args.Result = _principal is not null;
        }
        catch (OperationCanceledException) when (_sessionCancellation.IsCancellationRequested)
        {
            args.Result = false;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "SSH public key authentication failed unexpectedly.");
            args.Result = false;
        }
    }

    private void HandleCommandOpened(object? sender, SessionRequestedArgs args)
    {
        var principal = _principal
            ?? throw new SshConnectionException("SSH user is not authenticated.", DisconnectReason.NoMoreAuthMethodsAvailable);
        var parsedCommand = ParseCommand(args.CommandText);

        lock (_syncRoot)
        {
            if (_commandStarted)
            {
                throw new SshConnectionException(
                    "Only one Git command is allowed per SSH session.",
                    DisconnectReason.ByApplication);
            }

            _commandStarted = true;
        }

        var requiresWrite = parsedCommand.Service == GitTransportService.ReceivePack;
        var authorized = _accessService.CanAccessRepositoryAsync(
                principal,
                parsedCommand.RepositoryName,
                requiresWrite,
                _sessionCancellation.Token)
            .GetAwaiter()
            .GetResult();
        if (!authorized)
        {
            throw new SshConnectionException("Repository access denied.", DisconnectReason.ByApplication);
        }

        var repositoryPath = _pathResolver.ResolveRepositoryPath(parsedCommand.RepositoryName);
        var repository = new GitRepositoryContext(parsedCommand.RepositoryName, repositoryPath);
        _transportBackend.EnsureRepositoryExists(repository);

        lock (_syncRoot)
        {
            if (_disposed)
            {
                throw new SshConnectionException(
                    "The SSH server is stopping.",
                    DisconnectReason.ByApplication);
            }

            _channel = args.Channel;
            _inputPipe = new Pipe(new PipeOptions(
                pauseWriterThreshold: 1024 * 1024,
                resumeWriterThreshold: 512 * 1024,
                useSynchronizationContext: false));
            _channel.DataReceived += HandleChannelData;
            _channel.EofReceived += HandleChannelEof;
            _channel.CloseReceived += HandleChannelClose;

            _transportTask = RunTransportAsync(
                repository,
                parsedCommand.Service,
                principal.UserName,
                args.Channel.GitProtocolVersion);
        }
    }

    private void HandleChannelData(object? sender, byte[] data)
    {
        var pipe = _inputPipe;
        if (pipe is null || _sessionCancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            pipe.Writer.WriteAsync(data, _sessionCancellation.Token)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException) when (_sessionCancellation.IsCancellationRequested)
        {
        }
    }

    private void HandleChannelEof(object? sender, EventArgs args)
    {
        CompleteInput();
    }

    private void HandleChannelClose(object? sender, EventArgs args)
    {
        CompleteInput();
        _sessionCancellation.Cancel();
        EnsureSessionClosed();
    }

    private async Task RunTransportAsync(
        GitRepositoryContext repository,
        GitTransportService service,
        string actorName,
        string? protocolVersion)
    {
        var channel = _channel
            ?? throw new InvalidOperationException("The SSH channel is not initialized.");
        var pipe = _inputPipe
            ?? throw new InvalidOperationException("The SSH input pipe is not initialized.");
        uint exitCode = 0;

        try
        {
            await using var input = pipe.Reader.AsStream();
            await using var output = new SshChannelOutputStream(channel, _sessionCancellation.Token);
            var request = new GitTransportRequest(
                repository,
                service,
                StatelessRpc: false,
                AdvertiseRefs: false,
                ProtocolVersion: protocolVersion,
                actorName);
            await _transportBackend.ExecuteAsync(
                request,
                input,
                output,
                _sessionCancellation.Token);
        }
        catch (OperationCanceledException) when (_sessionCancellation.IsCancellationRequested)
        {
            exitCode = 1;
            _logger.LogInformation(
                "SSH Git {Service} for repository {RepositoryName} was canceled.",
                service,
                repository.RepositoryName);
        }
        catch (Exception exception)
        {
            exitCode = 1;
            _logger.LogError(
                exception,
                "SSH Git {Service} for repository {RepositoryName} failed.",
                service,
                repository.RepositoryName);
        }
        finally
        {
            CompleteInput();
            try
            {
                channel.SendEof();
                channel.SendClose(exitCode);
            }
            catch (Exception exception) when (_sessionCancellation.IsCancellationRequested)
            {
                _logger.LogDebug(exception, "SSH channel closed while the Git transport was stopping.");
            }

            EnsureSessionClosed();
        }
    }

    private void CompleteInput()
    {
        Pipe? pipe;
        lock (_syncRoot)
        {
            if (_inputCompleted)
            {
                return;
            }

            _inputCompleted = true;
            pipe = _inputPipe;
        }

        pipe?.Writer.Complete();
    }

    private void EnsureSessionClosed()
    {
        var channel = _channel;
        if (channel is not null && channel.ClientClosed && channel.ServerClosed)
        {
            _session.Disconnect();
        }
    }

    private static ParsedGitCommand ParseCommand(string commandText)
    {
        var match = GitCommandPattern.Match(commandText);
        if (!match.Success)
        {
            throw new SshConnectionException(
                "Only Git upload-pack, receive-pack and upload-archive commands are allowed.",
                DisconnectReason.ByApplication);
        }

        var service = match.Groups["command"].Value switch
        {
            "git-upload-pack" => GitTransportService.UploadPack,
            "git-receive-pack" => GitTransportService.ReceivePack,
            "git-upload-archive" => GitTransportService.UploadArchive,
            _ => throw new InvalidOperationException("The SSH Git command was not recognized.")
        };
        return new ParsedGitCommand(service, match.Groups["repository"].Value);
    }

    private sealed record ParsedGitCommand(GitTransportService Service, string RepositoryName);
}
