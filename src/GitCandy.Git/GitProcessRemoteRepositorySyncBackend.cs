using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using GitCandy.Configuration;
using GitCandy.Remotes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitCandy.Git;

/// <summary>通过受控 Git 子进程执行远程 fetch/push。</summary>
public sealed class GitProcessRemoteRepositorySyncBackend(
    IManagedGitRepositoryService repositoryService,
    IGitExecutableResolver executableResolver,
    IGitCandyApplicationPaths applicationPaths,
    IOptions<RemoteRepositorySyncOptions> options,
    IOptions<RemoteGitCredentialHelperOptions> credentialHelperOptions,
    ILogger<GitProcessRemoteRepositorySyncBackend> logger)
    : IRemoteRepositorySyncBackend
{
    private readonly IManagedGitRepositoryService _repositoryService = repositoryService;
    private readonly IGitExecutableResolver _executableResolver = executableResolver;
    private readonly IGitCandyApplicationPaths _applicationPaths = applicationPaths;
    private readonly RemoteRepositorySyncOptions _options = options.Value;
    private readonly RemoteGitCredentialHelperOptions _credentialHelperOptions = credentialHelperOptions.Value;
    private readonly ILogger<GitProcessRemoteRepositorySyncBackend> _logger = logger;

    /// <inheritdoc />
    public async Task<RemoteRepositorySyncResult> ExecuteAsync(
        RemoteRepositorySyncRequest request,
        RemoteCredential credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credential);
        ValidateCredential(credential);

        var repositoryPath = _repositoryService.ResolveExistingPath(request.Repository);
        using var runtime = RemoteSyncRuntimeContext.Create(_applicationPaths);
        await using var credentialServer = new RemoteCredentialPipeServer(
            request.RemoteGitUrl,
            GetCredentialUsername(request.Provider),
            credential.Secret.Value);
        using var timeout = new CancellationTokenSource(_options.OperationTimeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        using var helperLifetime = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        var startedTimestamp = Stopwatch.GetTimestamp();

        _logger.LogInformation(
            "Starting remote {Operation} for repository {RepositoryName} with provider {Provider}.",
            request.Operation,
            request.Repository.RepositoryName,
            request.Provider);

        var startInfo = CreateStartInfo(
            request,
            repositoryPath,
            runtime,
            credentialServer.PipeName);
        var helperTask = credentialServer.ServeAsync(helperLifetime.Token);
        using var process = new Process
        {
            StartInfo = startInfo
        };
        try
        {
            if (!process.Start())
            {
                throw CreateStartException();
            }
            process.StandardInput.Close();
        }
        catch (RemoteRepositorySyncException)
        {
            helperLifetime.Cancel();
            await ObserveHelperAsync(helperTask);
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            helperLifetime.Cancel();
            await ObserveHelperAsync(helperTask);
            _logger.LogError(
                "The remote {Operation} Git process could not start for repository {RepositoryName}.",
                request.Operation,
                request.Repository.RepositoryName);
            throw CreateStartException();
        }

        var standardOutputTask = process.StandardOutput.BaseStream.CopyToAsync(
            Stream.Null,
            _options.StreamBufferSize,
            operation.Token);
        var standardErrorTask = CaptureStandardErrorAsync(
            process.StandardError,
            _options.MaxDiagnosticCharacters,
            operation.Token);
        var exitTask = process.WaitForExitAsync(operation.Token);

        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask, exitTask);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopProcessAsync(process);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            await StopProcessAsync(process);
            _logger.LogWarning(
                "Remote {Operation} timed out for repository {RepositoryName}.",
                request.Operation,
                request.Repository.RepositoryName);
            throw new RemoteRepositorySyncException(
                RemoteRepositorySyncErrorCodes.TimedOut,
                "The remote Git operation exceeded its configured timeout.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            await StopProcessAsync(process);
            _logger.LogError(
                "Remote {Operation} stream processing failed for repository {RepositoryName}.",
                request.Operation,
                request.Repository.RepositoryName);
            throw new RemoteRepositorySyncException(
                RemoteRepositorySyncErrorCodes.ProcessFailed,
                "The remote Git process stream failed.");
        }
        finally
        {
            helperLifetime.Cancel();
            await ObserveHelperAsync(helperTask);
        }

        var duration = Stopwatch.GetElapsedTime(startedTimestamp);
        if (process.ExitCode != 0)
        {
            var errorCode = ClassifyError(standardErrorTask.Result);
            _logger.LogWarning(
                "Remote {Operation} failed for repository {RepositoryName} with code {ErrorCode} and exit code {ExitCode}.",
                request.Operation,
                request.Repository.RepositoryName,
                errorCode,
                process.ExitCode);
            throw new RemoteRepositorySyncException(
                errorCode,
                "The remote Git operation failed.");
        }

        _logger.LogInformation(
            "Completed remote {Operation} for repository {RepositoryName} with provider {Provider} in {ElapsedMilliseconds} ms.",
            request.Operation,
            request.Repository.RepositoryName,
            request.Provider,
            duration.TotalMilliseconds);
        return new RemoteRepositorySyncResult(request.Operation, duration);
    }

    private ProcessStartInfo CreateStartInfo(
        RemoteRepositorySyncRequest request,
        string repositoryPath,
        RemoteSyncRuntimeContext runtime,
        string pipeName)
    {
        var helperCommand = CreateCredentialHelperCommand();
        var startInfo = new ProcessStartInfo
        {
            FileName = _executableResolver.Resolve(),
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            WorkingDirectory = repositoryPath
        };
        startInfo.ArgumentList.Add("--no-optional-locks");
        if (request.Operation == RemoteRepositorySyncOperation.Fetch)
        {
            startInfo.ArgumentList.Add("fetch");
            startInfo.ArgumentList.Add("--no-recurse-submodules");
            startInfo.ArgumentList.Add("--no-write-fetch-head");
            startInfo.ArgumentList.Add("--no-tags");
            if (request.Prune)
            {
                startInfo.ArgumentList.Add("--prune");
            }
        }
        else
        {
            startInfo.ArgumentList.Add("push");
            startInfo.ArgumentList.Add("--porcelain");
            startInfo.ArgumentList.Add("--no-verify");
        }

        startInfo.ArgumentList.Add(request.RemoteGitUrl.AbsoluteUri);
        foreach (var refSpec in request.RefSpecs)
        {
            startInfo.ArgumentList.Add(refSpec.ToArgument());
        }

        var configuration = new KeyValuePair<string, string>[]
        {
            new("credential.helper", string.Empty),
            new("credential.helper", helperCommand),
            new("credential.useHttpPath", "true"),
            new("core.hooksPath", runtime.HooksPath),
            new("http.followRedirects", "false"),
            new("protocol.allow", "never"),
            new("protocol.http.allow", "always"),
            new("protocol.https.allow", "always")
        };
        startInfo.Environment["GIT_CONFIG_COUNT"] = configuration.Length.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        for (var index = 0; index < configuration.Length; index++)
        {
            startInfo.Environment[$"GIT_CONFIG_KEY_{index}"] = configuration[index].Key;
            startInfo.Environment[$"GIT_CONFIG_VALUE_{index}"] = configuration[index].Value;
        }

        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_SYSTEM"] = runtime.EmptyConfigurationPath;
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = runtime.EmptyConfigurationPath;
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        startInfo.Environment["GIT_FLUSH"] = "1";
        startInfo.Environment[RemoteCredentialPipeServer.PipeNameEnvironmentVariable] = pipeName;
        return startInfo;
    }

    private string CreateCredentialHelperCommand()
    {
        if (string.IsNullOrWhiteSpace(_credentialHelperOptions.CommandAssemblyPath))
        {
            throw new RemoteRepositorySyncException(
                RemoteRepositorySyncErrorCodes.CredentialUnsupported,
                "The remote credential helper is not configured.");
        }

        var assemblyPath = Path.GetFullPath(_credentialHelperOptions.CommandAssemblyPath);
        if (!File.Exists(assemblyPath))
        {
            throw new RemoteRepositorySyncException(
                RemoteRepositorySyncErrorCodes.CredentialUnsupported,
                "The remote credential helper is unavailable.");
        }

        var applicationName = Path.GetFileNameWithoutExtension(assemblyPath);
        var appHostPath = Path.Combine(
            Path.GetDirectoryName(assemblyPath)!,
            OperatingSystem.IsWindows() ? applicationName + ".exe" : applicationName);
        return File.Exists(appHostPath)
            ? $"!exec {QuoteForSh(ToGitPath(appHostPath))} {RemoteGitCredentialHelperCommand.CommandName}"
            : $"!exec dotnet {QuoteForSh(ToGitPath(assemblyPath))} {RemoteGitCredentialHelperCommand.CommandName}";
    }

    private static void ValidateCredential(RemoteCredential credential)
    {
        if (credential.AuthenticationKind == RemoteAuthenticationKind.Ssh)
        {
            throw new RemoteRepositorySyncException(
                RemoteRepositorySyncErrorCodes.CredentialUnsupported,
                "SSH credentials are not supported by the HTTP credential helper.");
        }

        if (credential.ExpiresAt is not null && credential.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new RemoteRepositorySyncException(
                RemoteRepositorySyncErrorCodes.AuthenticationFailed,
                "The remote credential has expired.");
        }
    }

    private static string GetCredentialUsername(RemoteProviderKind provider) => provider switch
    {
        RemoteProviderKind.GitHub => "x-access-token",
        RemoteProviderKind.GitLab => "oauth2",
        RemoteProviderKind.Gitee => "oauth2",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported remote provider.")
    };

    private static async Task<string> CaptureStandardErrorAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(maximumCharacters);
        var buffer = new char[1024];
        int charactersRead;
        while ((charactersRead = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (charactersRead >= maximumCharacters)
            {
                builder.Clear();
                builder.Append(buffer, charactersRead - maximumCharacters, maximumCharacters);
                continue;
            }

            var overflow = builder.Length + charactersRead - maximumCharacters;
            if (overflow > 0)
            {
                builder.Remove(0, overflow);
            }
            builder.Append(buffer, 0, charactersRead);
        }

        return builder.ToString();
    }

    private static string ClassifyError(string standardError)
    {
        if (ContainsAny(standardError, "authentication failed", "could not read username", "401", "invalid credentials"))
        {
            return RemoteRepositorySyncErrorCodes.AuthenticationFailed;
        }
        if (ContainsAny(standardError, "forbidden", "access denied", "403", "not allowed"))
        {
            return RemoteRepositorySyncErrorCodes.AuthorizationDenied;
        }
        if (ContainsAny(standardError, "repository not found", "not found", "404"))
        {
            return RemoteRepositorySyncErrorCodes.RepositoryNotFound;
        }
        if (ContainsAny(standardError, "non-fast-forward", "fetch first"))
        {
            return RemoteRepositorySyncErrorCodes.NonFastForward;
        }
        if (ContainsAny(standardError, "certificate", "ssl", "tls"))
        {
            return RemoteRepositorySyncErrorCodes.TlsFailed;
        }
        if (ContainsAny(
            standardError,
            "could not resolve host",
            "failed to connect",
            "connection timed out",
            "connection reset",
            "network is unreachable"))
        {
            return RemoteRepositorySyncErrorCodes.NetworkFailed;
        }

        return RemoteRepositorySyncErrorCodes.ProcessFailed;
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static async Task ObserveHelperAsync(Task helperTask)
    {
        try
        {
            await helperTask;
        }
        catch (Exception exception) when (exception is OperationCanceledException
            or IOException
            or InvalidDataException
            or System.Text.Json.JsonException)
        {
            // The Git process result remains the authoritative, safely classified outcome.
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or TimeoutException)
        {
            // The original cancellation, timeout, or stream failure remains actionable.
        }
    }

    private static RemoteRepositorySyncException CreateStartException() => new(
        RemoteRepositorySyncErrorCodes.ProcessStartFailed,
        "The remote Git process could not be started.");

    private static string ToGitPath(string path) =>
        OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;

    private static string QuoteForSh(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed class RemoteSyncRuntimeContext : IDisposable
    {
        private RemoteSyncRuntimeContext(string operationPath, string hooksPath, string configurationPath)
        {
            OperationPath = operationPath;
            HooksPath = hooksPath;
            EmptyConfigurationPath = configurationPath;
        }

        public string OperationPath { get; }

        public string HooksPath { get; }

        public string EmptyConfigurationPath { get; }

        public static RemoteSyncRuntimeContext Create(IGitCandyApplicationPaths applicationPaths)
        {
            var root = applicationPaths.ResolvePathWithinCacheRoot("remote-sync-runtime");
            Directory.CreateDirectory(root);
            var operationPath = applicationPaths.ResolvePathWithinCacheRoot(
                Path.Combine("remote-sync-runtime", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(operationPath);
            var hooksPath = Path.Combine(operationPath, "hooks");
            Directory.CreateDirectory(hooksPath);
            var configurationPath = Path.Combine(operationPath, "gitconfig");
            using (File.Create(configurationPath))
            {
            }

            return new RemoteSyncRuntimeContext(operationPath, hooksPath, configurationPath);
        }

        public void Dispose()
        {
            try
            {
                File.Delete(EmptyConfigurationPath);
                Directory.Delete(HooksPath);
                Directory.Delete(OperationPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Runtime files contain no credentials; a later maintenance pass may remove leftovers.
            }
        }
    }
}
