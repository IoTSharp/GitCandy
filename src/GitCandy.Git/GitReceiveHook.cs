using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GitCandy.Governance;
using Microsoft.Extensions.Options;

namespace GitCandy.Git;

/// <summary>受控 pre-receive bridge 的启动配置。</summary>
public sealed class GitReceiveHookOptions
{
    public string CommandAssemblyPath { get; set; } = string.Empty;
    public string DatabaseProvider { get; set; } = string.Empty;
    public string DatabaseConnectionString { get; set; } = string.Empty;
}

/// <summary>为 receive-pack 注入 GitCandy pre-receive hook。</summary>
public interface IGitReceiveHookLauncher
{
    void Configure(ProcessStartInfo startInfo, GitTransportRequest request);
}

/// <summary>Git pre-receive 子命令入口。</summary>
public interface IGitReceiveHookRunner
{
    Task<int> ExecuteAsync(TextReader input, TextWriter error, CancellationToken cancellationToken = default);
}

internal sealed class GitReceiveHookLauncher(
    IOptions<GitReceiveHookOptions> options,
    IGitExecutableResolver executableResolver) : IGitReceiveHookLauncher
{
    private readonly GitReceiveHookOptions _options = options.Value;
    private readonly IGitExecutableResolver _executableResolver = executableResolver;
    private readonly object _syncRoot = new();
    private string? _hookDirectory;

    public void Configure(ProcessStartInfo startInfo, GitTransportRequest request)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(request);
        if (request.RepositoryId <= 0
            || request.Actor is null
            || string.IsNullOrWhiteSpace(_options.CommandAssemblyPath))
        {
            return;
        }

        var hookDirectory = EnsureHookDirectory();
        var configCount = 0;
        if (startInfo.Environment.TryGetValue("GIT_CONFIG_COUNT", out var existingCount))
        {
            _ = int.TryParse(existingCount, NumberStyles.None, CultureInfo.InvariantCulture, out configCount);
        }
        startInfo.Environment[$"GIT_CONFIG_KEY_{configCount}"] = "core.hooksPath";
        startInfo.Environment[$"GIT_CONFIG_VALUE_{configCount}"] = hookDirectory;
        startInfo.Environment["GIT_CONFIG_COUNT"] = (configCount + 1).ToString(CultureInfo.InvariantCulture);
        startInfo.Environment["GITCANDY_REPOSITORY_ID"] = request.RepositoryId.ToString(CultureInfo.InvariantCulture);
        startInfo.Environment["GITCANDY_ACTOR_NAME"] = request.Actor.Name;
        startInfo.Environment["GITCANDY_ACTOR_USER_ID"] = request.Actor.UserId ?? string.Empty;
        startInfo.Environment["GITCANDY_DEPLOY_KEY_ID"] = request.Actor.DeployKeyId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        startInfo.Environment["GITCANDY_GIT_EXECUTABLE"] = _executableResolver.Resolve();
        startInfo.Environment["GitCandy__Database__Provider"] = _options.DatabaseProvider;
        startInfo.Environment["ConnectionStrings__GitCandy"] = _options.DatabaseConnectionString;
    }

    private string EnsureHookDirectory()
    {
        if (_hookDirectory is not null)
        {
            return _hookDirectory;
        }

        lock (_syncRoot)
        {
            if (_hookDirectory is not null)
            {
                return _hookDirectory;
            }

            var assemblyPath = Path.GetFullPath(_options.CommandAssemblyPath);
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GitCandy", "receive-hooks"));
            var instanceName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(assemblyPath)))[..16];
            var directory = Path.GetFullPath(Path.Combine(root, instanceName));
            if (!directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The Git hook directory escaped the managed temporary root.");
            }

            Directory.CreateDirectory(directory);
            var hookPath = Path.Combine(directory, "pre-receive");
            var script = CreateScript(assemblyPath);
            if (!File.Exists(hookPath) || !string.Equals(File.ReadAllText(hookPath), script, StringComparison.Ordinal))
            {
                var temporaryPath = hookPath + ".tmp-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
                File.WriteAllText(temporaryPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(temporaryPath, hookPath, overwrite: true);
            }
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    hookPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            _hookDirectory = directory;
            return directory;
        }
    }

    private static string CreateScript(string assemblyPath)
    {
        var applicationName = Path.GetFileNameWithoutExtension(assemblyPath);
        var appHostPath = Path.Combine(
            Path.GetDirectoryName(assemblyPath)!,
            OperatingSystem.IsWindows() ? applicationName + ".exe" : applicationName);
        return File.Exists(appHostPath)
            ? $"#!/bin/sh\nexec {QuoteForSh(ToGitPath(appHostPath))} --git-pre-receive\n"
            : $"#!/bin/sh\nexec dotnet {QuoteForSh(ToGitPath(assemblyPath))} --git-pre-receive\n";
    }

    private static string ToGitPath(string path)
    {
        return OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
    }

    private static string QuoteForSh(string value)
    {
        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}

internal sealed class GitReceiveHookRunner(IGitPushGate pushGate) : IGitReceiveHookRunner
{
    private const int MaxRefUpdates = 1024;
    private readonly IGitPushGate _pushGate = pushGate;

    public async Task<int> ExecuteAsync(
        TextReader input,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(error);
        if (!TryReadInvocation(out var repositoryId, out var actor))
        {
            await error.WriteLineAsync("GitCandy push gate context is invalid.");
            return 1;
        }

        var updates = new List<GitRefUpdate>();
        while (await input.ReadLineAsync(cancellationToken) is string line)
        {
            if (updates.Count >= MaxRefUpdates || !TryParseUpdate(line, out var update))
            {
                await error.WriteLineAsync("GitCandy push gate received an invalid ref update.");
                return 1;
            }

            if (update.ReferenceName.StartsWith("refs/heads/", StringComparison.Ordinal)
                && !update.IsDelete
                && !IsZeroObjectId(update.OldObjectId))
            {
                var force = await IsForceUpdateAsync(update.OldObjectId, update.NewObjectId, cancellationToken);
                if (force is null)
                {
                    await error.WriteLineAsync($"GitCandy could not validate ancestry for {update.ReferenceName}.");
                    return 1;
                }
                update = update with { IsForceUpdate = force.Value };
            }
            updates.Add(update);
        }

        var result = await _pushGate.EvaluateAsync(
            new GitPushGateRequest(repositoryId, actor, GitRefOperation.Push, updates),
            cancellationToken);
        if (result.Allowed)
        {
            return 0;
        }

        foreach (var reason in result.Reasons)
        {
            await error.WriteLineAsync("GitCandy push rejected: " + reason);
        }
        return 1;
    }

    private async Task<bool?> IsForceUpdateAsync(
        string oldObjectId,
        string newObjectId,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("GITCANDY_GIT_EXECUTABLE")
                ?? (OperatingSystem.IsWindows() ? "git.exe" : "git"),
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("merge-base");
        startInfo.ArgumentList.Add("--is-ancestor");
        startInfo.ArgumentList.Add(oldObjectId);
        startInfo.ArgumentList.Add(newObjectId);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) return null;
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode switch { 0 => false, 1 => true, _ => null };
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool TryReadInvocation(out long repositoryId, out GitRefActor actor)
    {
        var userId = NullIfEmpty(Environment.GetEnvironmentVariable("GITCANDY_ACTOR_USER_ID"));
        var deployKeyValue = Environment.GetEnvironmentVariable("GITCANDY_DEPLOY_KEY_ID");
        long? deployKeyId = long.TryParse(deployKeyValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedDeployKeyId)
            && parsedDeployKeyId > 0 ? parsedDeployKeyId : null;
        var actorName = Environment.GetEnvironmentVariable("GITCANDY_ACTOR_NAME")?.Trim();
        var valid = long.TryParse(
            Environment.GetEnvironmentVariable("GITCANDY_REPOSITORY_ID"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out repositoryId)
            && repositoryId > 0
            && !string.IsNullOrWhiteSpace(actorName)
            && !(userId is not null && deployKeyId is not null);
        actor = new GitRefActor(actorName ?? "unknown", userId, deployKeyId);
        return valid;
    }

    private static bool TryParseUpdate(string line, out GitRefUpdate update)
    {
        var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3
            || parts[0].Length != parts[1].Length
            || parts[0].Length is not (40 or 64)
            || !parts[0].All(Uri.IsHexDigit)
            || !parts[1].All(Uri.IsHexDigit)
            || !parts[2].StartsWith("refs/", StringComparison.Ordinal)
            || parts[2].Length > 255
            || parts[2].Any(static character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            update = null!;
            return false;
        }

        update = new GitRefUpdate(parts[0], parts[1], parts[2]);
        return true;
    }

    private static bool IsZeroObjectId(string objectId)
    {
        return objectId.All(static character => character == '0');
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
