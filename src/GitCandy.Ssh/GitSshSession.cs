using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.RegularExpressions;
using GitCandy.Git;
using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Events;
using Microsoft.DevTunnels.Ssh.Messages;

namespace GitCandy.Ssh;

internal sealed class GitSshSession : IDisposable
{
    private static readonly Regex GitCommandPattern = new(
        "^(?<command>git-upload-pack|git-receive-pack|git-upload-archive) '/?git/(?<repository>[^/\\\\'\\r\\n]+)\\.git'$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private readonly SshServerSession _session;
    private readonly ISshAccessService _accessService;
    private readonly IGitRepositoryPathResolver _pathResolver;
    private readonly IGitTransportBackend _transportBackend;
    private readonly ILogger<GitSshSession> _logger;
    private readonly CancellationTokenSource _sessionCancellation;
    private readonly object _syncRoot = new();
    private SshChannel? _channel;
    private Task? _transportTask;
    private SshPrincipal? _principal;
    private string? _gitProtocolVersion;
    private bool _commandStarted;
    private bool _disposed;

    public GitSshSession(
        SshServerSession session,
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
        _session.Authenticating += HandleAuthenticating;
        _session.ChannelOpening += HandleChannelOpening;
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

        _session.Authenticating -= HandleAuthenticating;
        _session.ChannelOpening -= HandleChannelOpening;
        if (_channel is not null)
        {
            _channel.Request -= HandleChannelRequest;
            _channel.Closed -= HandleChannelClosed;
        }

        _sessionCancellation.Cancel();
        if (transportTask is null || transportTask.IsCompleted)
        {
            _sessionCancellation.Dispose();
        }
        else
        {
            _ = transportTask.ContinueWith(
                static (_, state) => ((CancellationTokenSource?)state)?.Dispose(),
                _sessionCancellation,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void HandleAuthenticating(object? sender, SshAuthenticatingEventArgs args)
    {
        if (args.AuthenticationType is not (
                SshAuthenticationType.ClientPublicKeyQuery
                or SshAuthenticationType.ClientPublicKey)
            || args.PublicKey is null)
        {
            return;
        }

        args.AuthenticationTask = AuthenticateAsync(args);
    }

    private async Task<ClaimsPrincipal?> AuthenticateAsync(SshAuthenticatingEventArgs args)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _sessionCancellation.Token,
            args.Cancellation);
        try
        {
            var publicKey = args.PublicKey;
            if (publicKey is null)
            {
                return null;
            }
            var principal = await _accessService.AuthenticateAsync(
                publicKey.KeyAlgorithmName,
                publicKey.GetPublicKeyBytes().ToArray(),
                recordUsage: args.AuthenticationType == SshAuthenticationType.ClientPublicKey,
                cancellationToken: linkedCancellation.Token);
            if (principal is null)
            {
                return null;
            }

            if (args.AuthenticationType == SshAuthenticationType.ClientPublicKey)
            {
                _principal = principal;
            }

            var authenticationType = args.AuthenticationType == SshAuthenticationType.ClientPublicKey
                ? "GitCandy.Ssh.PublicKey"
                : null;
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, principal.UserId),
                    new Claim(ClaimTypes.Name, principal.UserName)
                ],
                authenticationType);
            return new ClaimsPrincipal(identity);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "SSH public key authentication failed unexpectedly.");
            return null;
        }
    }

    private void HandleChannelOpening(object? sender, SshChannelOpeningEventArgs args)
    {
        if (!args.IsRemoteRequest
            || !string.Equals(args.Channel.ChannelType, SshChannel.SessionChannelType, StringComparison.Ordinal))
        {
            args.FailureReason = SshChannelOpenFailureReason.AdministrativelyProhibited;
            args.FailureDescription = "Only SSH session channels for Git commands are allowed.";
            return;
        }

        lock (_syncRoot)
        {
            if (_channel is not null || _disposed)
            {
                args.FailureReason = SshChannelOpenFailureReason.ResourceShortage;
                args.FailureDescription = "Only one channel is allowed per SSH session.";
                return;
            }

            _channel = args.Channel;
            _channel.Request += HandleChannelRequest;
            _channel.Closed += HandleChannelClosed;
        }
    }

    private void HandleChannelRequest(
        object? sender,
        SshRequestEventArgs<ChannelRequestMessage> args)
    {
        if (sender is not SshChannel channel || !ReferenceEquals(channel, _channel))
        {
            return;
        }

        if (string.Equals(args.RequestType, GitEnvironmentRequestMessage.EnvironmentRequestType, StringComparison.Ordinal))
        {
            var environment = args.Request.ConvertTo<GitEnvironmentRequestMessage>();
            args.IsAuthorized = string.Equals(environment.VariableName, "GIT_PROTOCOL", StringComparison.Ordinal)
                && string.Equals(environment.VariableValue, "version=2", StringComparison.Ordinal);
            if (args.IsAuthorized)
            {
                lock (_syncRoot)
                {
                    if (_commandStarted || _disposed)
                    {
                        args.IsAuthorized = false;
                    }
                    else
                    {
                        _gitProtocolVersion = environment.VariableValue;
                    }
                }
            }

            return;
        }

        if (!string.Equals(args.RequestType, ChannelRequestTypes.Command, StringComparison.Ordinal))
        {
            return;
        }

        var commandRequest = args.Request.ConvertTo<CommandRequestMessage>();
        if (!TryParseCommand(commandRequest.Command, out var parsedCommand))
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_commandStarted || _disposed)
            {
                return;
            }

            _commandStarted = true;
        }

        var authorizationTask = AuthorizeCommandAsync(parsedCommand, args.Cancellation);
        args.ResponseTask = GetAuthorizationResponseAsync(authorizationTask);
        args.ResponseContinuation = response =>
        {
            if (response is ChannelSuccessMessage
                && authorizationTask.IsCompletedSuccessfully
                && authorizationTask.Result is AuthorizedGitCommand authorizedCommand)
            {
                lock (_syncRoot)
                {
                    if (!_disposed)
                    {
                        _transportTask = RunTransportAsync(channel, authorizedCommand);
                    }
                }
            }

            return Task.CompletedTask;
        };
    }

    private async Task<AuthorizedGitCommand?> AuthorizeCommandAsync(
        ParsedGitCommand parsedCommand,
        CancellationToken requestCancellation)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _sessionCancellation.Token,
            requestCancellation);
        try
        {
            var principal = _principal;
            if (principal is null)
            {
                return null;
            }

            var requiresWrite = parsedCommand.Service == GitTransportService.ReceivePack;
            if (!await _accessService.CanAccessRepositoryAsync(
                    principal,
                    parsedCommand.RepositoryName,
                    requiresWrite,
                    linkedCancellation.Token))
            {
                return null;
            }

            var repositoryPath = _pathResolver.ResolveRepositoryPath(parsedCommand.RepositoryName);
            var repository = new GitRepositoryContext(parsedCommand.RepositoryName, repositoryPath);
            _transportBackend.EnsureRepositoryExists(repository);
            return new AuthorizedGitCommand(
                repository,
                parsedCommand.Service,
                principal.UserName,
                _gitProtocolVersion);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (GitRepositoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "SSH Git {Service} authorization for repository {RepositoryName} failed unexpectedly.",
                parsedCommand.Service,
                parsedCommand.RepositoryName);
            return null;
        }
    }

    private static async Task<SshMessage> GetAuthorizationResponseAsync(
        Task<AuthorizedGitCommand?> authorizationTask)
    {
        return await authorizationTask is null
            ? new ChannelFailureMessage()
            : new ChannelSuccessMessage();
    }

    private async Task RunTransportAsync(SshChannel channel, AuthorizedGitCommand command)
    {
        uint exitCode = 0;
        var input = new SshStream(channel);
        try
        {
            await using var output = new SshChannelOutputStream(channel, _sessionCancellation.Token);
            var request = new GitTransportRequest(
                command.Repository,
                command.Service,
                StatelessRpc: false,
                AdvertiseRefs: false,
                command.ProtocolVersion,
                command.ActorName);
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
                command.Service,
                command.Repository.RepositoryName);
        }
        catch (Exception exception)
        {
            exitCode = 1;
            _logger.LogError(
                exception,
                "SSH Git {Service} for repository {RepositoryName} failed.",
                command.Service,
                command.Repository.RepositoryName);
        }
        finally
        {
            try
            {
                await channel.CloseAsync(exitCode, CancellationToken.None);
            }
            catch (Exception exception) when (_sessionCancellation.IsCancellationRequested)
            {
                _logger.LogDebug(exception, "SSH channel closed while the Git transport was stopping.");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "SSH channel could not send its final exit status.");
            }
            finally
            {
                input.Dispose();
            }
        }
    }

    private void HandleChannelClosed(object? sender, SshChannelClosedEventArgs args)
    {
        _sessionCancellation.Cancel();
    }

    private static bool TryParseCommand(
        string? commandText,
        [NotNullWhen(true)] out ParsedGitCommand? parsedCommand)
    {
        var match = GitCommandPattern.Match(commandText ?? string.Empty);
        if (!match.Success)
        {
            parsedCommand = null;
            return false;
        }

        var service = match.Groups["command"].Value switch
        {
            "git-upload-pack" => GitTransportService.UploadPack,
            "git-receive-pack" => GitTransportService.ReceivePack,
            "git-upload-archive" => GitTransportService.UploadArchive,
            _ => throw new InvalidOperationException("The SSH Git command was not recognized.")
        };
        parsedCommand = new ParsedGitCommand(service, match.Groups["repository"].Value);
        return true;
    }

    private sealed record ParsedGitCommand(GitTransportService Service, string RepositoryName);

    private sealed record AuthorizedGitCommand(
        GitRepositoryContext Repository,
        GitTransportService Service,
        string ActorName,
        string? ProtocolVersion);
}
