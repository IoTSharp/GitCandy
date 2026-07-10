using GitCandy.Configuration;
using GitCandy.Git;

namespace GitCandy.Ssh;

/// <summary>
/// 将 OpenSSH public key 认证结果桥接到 GitCandy 权限和 Git transport。
/// </summary>
public sealed class OpenSshAdapter(
    ISshAccessService accessService,
    IGitServiceFactory gitServiceFactory,
    IGitTransportBackend transportBackend,
    IOptions<GitCandyOpenSshOptions> options,
    ILogger<OpenSshAdapter> logger) : IOpenSshAdapter
{
    private readonly ISshAccessService _accessService = accessService;
    private readonly IGitServiceFactory _gitServiceFactory = gitServiceFactory;
    private readonly IGitTransportBackend _transportBackend = transportBackend;
    private readonly GitCandyOpenSshOptions _options = options.Value;
    private readonly ILogger<OpenSshAdapter> _logger = logger;

    /// <inheritdoc />
    public async Task<int> WriteAuthorizedKeyAsync(
        string fingerprint,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(output);

        if (!IsEnabled())
        {
            return 1;
        }

        var key = await _accessService.FindAuthorizedKeyAsync(
            fingerprint,
            recordUsage: false,
            cancellationToken);
        if (key is null || !IsValidAuthorizedKey(key))
        {
            return 1;
        }

        var executablePath = Path.GetFullPath(_options.ExecutablePath);
        var forcedCommand = $"{QuoteCommandArgument(executablePath)} --openssh-forced-command SHA256:{key.Fingerprint}";
        var authorizedKeysCommand = forcedCommand
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        await output.WriteLineAsync(
            $"restrict,command=\"{authorizedKeysCommand}\" {key.KeyType} {key.PublicKey}");
        return 0;
    }

    /// <inheritdoc />
    public async Task<int> ExecuteForcedCommandAsync(
        string fingerprint,
        string? originalCommand,
        string? gitProtocol,
        Stream input,
        Stream output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!IsEnabled())
        {
            await error.WriteLineAsync("GitCandy OpenSSH adapter is disabled.");
            return 1;
        }

        if (!GitSshCommandParser.TryParse(originalCommand, out var parsedCommand))
        {
            await error.WriteLineAsync("Only Git transport commands are allowed.");
            return 1;
        }

        if (gitProtocol is not null
            && !string.Equals(gitProtocol, "version=2", StringComparison.Ordinal))
        {
            await error.WriteLineAsync("The requested Git protocol version is not supported.");
            return 1;
        }

        try
        {
            var key = await _accessService.FindAuthorizedKeyAsync(
                fingerprint,
                recordUsage: true,
                cancellationToken);
            if (key is null)
            {
                await error.WriteLineAsync("SSH key authentication failed.");
                return 1;
            }

            var requiresWrite = parsedCommand.Service == GitTransportService.ReceivePack;
            if (!await _accessService.CanAccessRepositoryAsync(
                    key.Principal,
                    parsedCommand.RepositoryName,
                    requiresWrite,
                    cancellationToken))
            {
                await error.WriteLineAsync("Repository access denied.");
                return 1;
            }

            var repository = _gitServiceFactory.Create(parsedCommand.RepositoryName);
            _transportBackend.EnsureRepositoryExists(repository);
            var request = new GitTransportRequest(
                repository,
                parsedCommand.Service,
                StatelessRpc: false,
                AdvertiseRefs: false,
                gitProtocol,
                key.Principal.UserName);
            await _transportBackend.ExecuteAsync(request, input, output, cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GitRepositoryNotFoundException)
        {
            await error.WriteLineAsync("Repository not found.");
            return 1;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "OpenSSH forced Git command failed.");
            await error.WriteLineAsync("Git transport failed.");
            return 1;
        }
    }

    private bool IsEnabled()
    {
        return _options.Enabled
            && Path.IsPathFullyQualified(_options.ExecutablePath);
    }

    private static bool IsValidAuthorizedKey(SshAuthorizedKey key)
    {
        if (key.KeyType.Length == 0
            || key.KeyType.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '@'))
            || key.PublicKey.Any(char.IsWhiteSpace))
        {
            return false;
        }

        try
        {
            _ = Convert.FromBase64String(key.PublicKey);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string QuoteCommandArgument(string value)
    {
        return $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }
}
