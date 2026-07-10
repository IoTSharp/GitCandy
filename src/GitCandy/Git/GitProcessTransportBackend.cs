using System.Diagnostics;
using System.Text;
using GitCandy.Configuration;
using Microsoft.Extensions.Options;

namespace GitCandy.Git;

/// <summary>
/// 通过结构化参数调用 Git 官方 helper 的流式 transport backend。
/// </summary>
public sealed class GitProcessTransportBackend(
    IGitRepositoryPathResolver pathResolver,
    IGitExecutableResolver executableResolver,
    IOptions<GitSmartHttpOptions> options,
    ILogger<GitProcessTransportBackend> logger)
    : IGitTransportBackend, IDisposable
{
    private const int MaxCapturedStandardErrorLength = 8192;
    private readonly IGitRepositoryPathResolver _pathResolver = pathResolver;
    private readonly IGitExecutableResolver _executableResolver = executableResolver;
    private readonly GitSmartHttpOptions _options = options.Value;
    private readonly ILogger<GitProcessTransportBackend> _logger = logger;
    private readonly SemaphoreSlim _operationSlots = new(options.Value.MaxConcurrentOperations);

    /// <inheritdoc />
    public void EnsureRepositoryExists(GitRepositoryContext repository)
    {
        _ = ResolveExistingRepositoryPath(repository);
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(
        GitTransportRequest request,
        Stream input,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        await _operationSlots.WaitAsync(cancellationToken);
        try
        {
            await ExecuteCoreAsync(request, input, output, cancellationToken);
        }
        finally
        {
            _operationSlots.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _operationSlots.Dispose();
    }

    private async Task ExecuteCoreAsync(
        GitTransportRequest request,
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        var startedTimestamp = Stopwatch.GetTimestamp();
        _logger.LogInformation(
            "Starting Git {Service} for repository {RepositoryName} as {ActorName}. Advertise refs: {AdvertiseRefs}.",
            request.Service,
            request.Repository.RepositoryName,
            request.ActorName,
            request.AdvertiseRefs);

        var repositoryPath = ResolveExistingRepositoryPath(request.Repository);
        var startInfo = CreateStartInfo(request, repositoryPath);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new GitTransportException("The Git helper process did not start.");
            }
        }
        catch (GitTransportException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(
                exception,
                "Failed to start Git {Service} for repository {RepositoryName}.",
                request.Service,
                request.Repository.RepositoryName);
            throw new GitTransportException("The Git helper process could not be started.", exception);
        }

        var inputTask = CopyInputAndCloseAsync(
            input,
            process.StandardInput,
            _options.StreamBufferSize,
            cancellationToken);
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(
            output,
            _options.StreamBufferSize,
            cancellationToken);
        var standardErrorTask = CaptureStandardErrorAsync(
            process.StandardError,
            cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);

        try
        {
            await Task.WhenAll(inputTask, outputTask, standardErrorTask, exitTask);
        }
        catch (OperationCanceledException)
        {
            await StopProcessAsync(process);
            throw;
        }
        catch (InvalidDataException)
        {
            await StopProcessAsync(process);
            throw;
        }
        catch (Exception exception)
        {
            await StopProcessAsync(process);
            var standardError = standardErrorTask.IsCompletedSuccessfully
                ? SanitizeStandardError(standardErrorTask.Result, repositoryPath)
                : string.Empty;
            LogProcessFailure(request, process, standardError, exception);
            throw new GitTransportException("The Git helper stream failed.", exception);
        }

        var capturedStandardError = SanitizeStandardError(
            await standardErrorTask,
            repositoryPath);
        if (process.ExitCode == 0)
        {
            _logger.LogInformation(
                "Completed Git {Service} for repository {RepositoryName} as {ActorName} in {ElapsedMilliseconds} ms.",
                request.Service,
                request.Repository.RepositoryName,
                request.ActorName,
                Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds);
            return;
        }

        LogProcessFailure(request, process, capturedStandardError, exception: null);
        throw new GitTransportException(
            $"The Git helper exited with code {process.ExitCode}.");
    }

    private ProcessStartInfo CreateStartInfo(
        GitTransportRequest request,
        string repositoryPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executableResolver.Resolve(),
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = _pathResolver.RepositoryRootPath
        };

        startInfo.ArgumentList.Add(GetCommandName(request.Service));
        if (request.StatelessRpc)
        {
            startInfo.ArgumentList.Add("--stateless-rpc");
        }

        if (request.AdvertiseRefs)
        {
            startInfo.ArgumentList.Add("--advertise-refs");
        }

        startInfo.ArgumentList.Add(repositoryPath);

        if (request.ProtocolVersion is not null)
        {
            startInfo.Environment["GIT_PROTOCOL"] = request.ProtocolVersion;
        }

        return startInfo;
    }

    private string ResolveExistingRepositoryPath(GitRepositoryContext repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var repositoryName = repository.RepositoryName.Trim();
        var legacyPath = _pathResolver.ResolveRepositoryPath(repositoryName);
        var dotGitPath = _pathResolver.ResolveRepositoryPath($"{repositoryName}.git");
        var configuredPath = Path.GetFullPath(repository.RepositoryPath);

        if (!PathsEqual(configuredPath, legacyPath)
            && !PathsEqual(configuredPath, dotGitPath))
        {
            throw new InvalidOperationException(
                "The Git repository context did not originate from the configured repository root.");
        }

        foreach (var candidate in EnumerateDistinctPaths(configuredPath, legacyPath, dotGitPath))
        {
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            EnsureResolvedPathWithinRoot(candidate);
            return candidate;
        }

        throw new GitRepositoryNotFoundException(repository.RepositoryName);
    }

    private void EnsureResolvedPathWithinRoot(string repositoryPath)
    {
        var rootPath = ResolveFinalDirectoryTarget(_pathResolver.RepositoryRootPath);
        var resolvedRepositoryPath = ResolveFinalDirectoryTarget(repositoryPath);
        var relativePath = Path.GetRelativePath(rootPath, resolvedRepositoryPath);

        if (relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException(
                "The resolved Git repository path escapes the configured repository root.");
        }
    }

    private static string ResolveFinalDirectoryTarget(string path)
    {
        var directory = new DirectoryInfo(path);
        var target = directory.ResolveLinkTarget(returnFinalTarget: true);
        return Path.GetFullPath(target?.FullName ?? directory.FullName);
    }

    private static IEnumerable<string> EnumerateDistinctPaths(params string[] paths)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return paths.Distinct(comparer);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static string GetCommandName(GitTransportService service)
    {
        return service switch
        {
            GitTransportService.UploadPack => "upload-pack",
            GitTransportService.ReceivePack => "receive-pack",
            GitTransportService.UploadArchive => "upload-archive",
            _ => throw new ArgumentOutOfRangeException(nameof(service), service, null)
        };
    }

    private static async Task CopyInputAndCloseAsync(
        Stream input,
        StreamWriter standardInput,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await input.CopyToAsync(
                standardInput.BaseStream,
                bufferSize,
                cancellationToken);
        }
        finally
        {
            standardInput.Close();
        }
    }

    private static async Task<string> CaptureStandardErrorAsync(
        StreamReader standardError,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(MaxCapturedStandardErrorLength);
        var buffer = new char[1024];
        int charactersRead;
        while ((charactersRead = await standardError.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var remaining = MaxCapturedStandardErrorLength - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(remaining, charactersRead));
            }
        }

        return builder.ToString();
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
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            // The original transport failure remains the actionable error.
        }
    }

    private static string SanitizeStandardError(string standardError, string repositoryPath)
    {
        if (string.IsNullOrEmpty(standardError))
        {
            return string.Empty;
        }

        return standardError.Replace(
            repositoryPath,
            "[repository]",
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private void LogProcessFailure(
        GitTransportRequest request,
        Process process,
        string standardError,
        Exception? exception)
    {
        var exitCode = process.HasExited ? process.ExitCode : -1;
        _logger.LogError(
            exception,
            "Git {Service} failed for repository {RepositoryName} with exit code {ExitCode}. Stderr: {StandardError}",
            request.Service,
            request.Repository.RepositoryName,
            exitCode,
            standardError);
    }
}
