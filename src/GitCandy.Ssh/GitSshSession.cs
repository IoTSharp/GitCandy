using System.Security.Claims;
using System.Text;
using GitCandy.Git;
using GitCandy.Issues;
using Microsoft.DevTunnels.Ssh;
using Microsoft.DevTunnels.Ssh.Events;
using Microsoft.DevTunnels.Ssh.Messages;
using SshBuffer = Microsoft.DevTunnels.Ssh.Buffer;

namespace GitCandy.Ssh;

internal sealed class GitSshSession : IDisposable
{
    private readonly SshServerSession _session;
    private readonly ISshAccessService _accessService;
    private readonly IGitRepositoryPathResolver _pathResolver;
    private readonly IGitTransportBackend _transportBackend;
    private readonly IRepositoryBrowserService _repositoryBrowserService;
    private readonly IIssueService _issueService;
    private readonly GitSshExtendedDataSender _extendedDataSender;
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
        IRepositoryBrowserService repositoryBrowserService,
        IIssueService issueService,
        ILogger<GitSshSession> logger)
    {
        _session = session;
        _accessService = accessService;
        _pathResolver = pathResolver;
        _transportBackend = transportBackend;
        _repositoryBrowserService = repositoryBrowserService;
        _issueService = issueService;
        _extendedDataSender = new GitSshExtendedDataSender(session);
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
        if (!GitSshCommandParser.TryParse(commandRequest.Command, out var parsedCommand))
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
            var address = await _accessService.ResolveRepositoryAsync(
                parsedCommand.NamespaceSlug,
                parsedCommand.RepositorySlug,
                parsedCommand.IsLegacy,
                linkedCancellation.Token);
            if (address is null)
            {
                return null;
            }

            if (!await _accessService.CanAccessRepositoryAsync(
                    principal,
                    address.RepositoryId,
                    requiresWrite,
                    linkedCancellation.Token))
            {
                return null;
            }

            var repositoryPath = _pathResolver.ResolveRepositoryPath(address.StorageName);
            var repository = new GitRepositoryContext(address.StorageName, repositoryPath);
            _transportBackend.EnsureRepositoryExists(repository);
            return new AuthorizedGitCommand(
                repository,
                address.RepositoryId,
                parsedCommand.Service,
                principal.UserId,
                principal.UserName,
                _gitProtocolVersion,
                address.UsedAlias ? address.CanonicalPath : null);
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
                parsedCommand.RepositorySlug);
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
            if (command.Service == GitTransportService.ReceivePack)
            {
                await ApplyClosingReferencesAsync(command);
            }
            if (command.CanonicalPath is not null)
            {
                await SendCanonicalAddressWarningAsync(channel, command.CanonicalPath);
            }
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

    private async Task SendCanonicalAddressWarningAsync(SshChannel channel, string canonicalPath)
    {
        var warning = $"Repository address changed; update the remote to {canonicalPath}.git.\n";
        var message = new GitSshExtendedDataMessage
        {
            RecipientChannel = channel.RemoteChannelId,
            DataTypeCode = 1,
            Data = SshBuffer.From(Encoding.UTF8.GetBytes(warning))
        };
        await _extendedDataSender.SendAsync(message, _sessionCancellation.Token);
        _logger.LogInformation(
            "SSH repository address changed; sent the canonical remote {CanonicalPath}.git to the client.",
            canonicalPath);
    }

    private async Task ApplyClosingReferencesAsync(AuthorizedGitCommand command)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var commit = _repositoryBrowserService.ReadCommits(
                command.Repository, revision: null, page: 1, pageSize: 1, timeout.Token)?.Commits.FirstOrDefault();
            if (commit is not null)
            {
                await _issueService.ApplyClosingReferencesAsync(
                    command.RepositoryId,
                    command.ActorUserId,
                    commit.Message,
                    commit.Id,
                    timeout.Token);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Issue closing references could not be processed after a successful SSH push.");
        }
    }

    private sealed record AuthorizedGitCommand(
        GitRepositoryContext Repository,
        long RepositoryId,
        GitTransportService Service,
        string ActorUserId,
        string ActorName,
        string? ProtocolVersion,
        string? CanonicalPath);
}
