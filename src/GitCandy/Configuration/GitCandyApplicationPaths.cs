using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace GitCandy.Configuration;

/// <summary>
/// 基于 ASP.NET Core 宿主环境解析 GitCandy 应用路径配置。
/// </summary>
public sealed class GitCandyApplicationPaths : IGitCandyApplicationPaths
{
    private const string RelativePathEscapesRootMessage =
        "Configured relative paths must stay inside their host root. Use a fully qualified path for external locations.";

    /// <summary>
    /// 初始化 GitCandy 应用路径解析结果。
    /// </summary>
    /// <param name="environment">ASP.NET Core Web 宿主环境。</param>
    /// <param name="options">GitCandy 应用配置。</param>
    public GitCandyApplicationPaths(
        IWebHostEnvironment environment,
        IOptions<GitCandyApplicationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        ContentRootPath = NormalizeRootPath(environment.ContentRootPath);
        WebRootPath = string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? null
            : NormalizeRootPath(environment.WebRootPath);

        var value = options.Value;
        LogPathFormat = ResolveContentPath(value.LogPathFormat);
        UserConfigurationPath = ResolveContentPath(value.UserConfigurationPath);
        RepositoryPath = ResolveContentPath(value.RepositoryPath);
        CachePath = ResolveContentPath(value.CachePath);
        GitCorePath = string.IsNullOrWhiteSpace(value.GitCorePath)
            ? string.Empty
            : ResolveContentPath(value.GitCorePath);
        SshHostKeyPath = ResolveContentPath(value.SshHostKeyPath);
        DataProtectionKeysPath = ResolveContentPath(value.DataProtectionKeysPath);
    }

    /// <inheritdoc />
    public string ContentRootPath { get; }

    /// <inheritdoc />
    public string? WebRootPath { get; }

    /// <inheritdoc />
    public string LogPathFormat { get; }

    /// <inheritdoc />
    public string UserConfigurationPath { get; }

    /// <inheritdoc />
    public string RepositoryPath { get; }

    /// <inheritdoc />
    public string CachePath { get; }

    /// <inheritdoc />
    public string GitCorePath { get; }

    /// <inheritdoc />
    public string SshHostKeyPath { get; }

    /// <inheritdoc />
    public string DataProtectionKeysPath { get; }

    /// <inheritdoc />
    public string ResolveContentPath(string configuredPath)
    {
        return ResolvePath(configuredPath, ContentRootPath);
    }

    /// <inheritdoc />
    public string ResolveWebRootPath(string configuredPath)
    {
        if (WebRootPath is null)
        {
            throw new InvalidOperationException("The ASP.NET Core web root path is not configured.");
        }

        return ResolvePath(configuredPath, WebRootPath);
    }

    /// <inheritdoc />
    public string ResolvePathWithinRepositoryRoot(string path)
    {
        return ResolvePathWithinRoot(path, RepositoryPath, "repository");
    }

    /// <inheritdoc />
    public string ResolvePathWithinCacheRoot(string path)
    {
        return ResolvePathWithinRoot(path, CachePath, "cache");
    }

    private static string ResolvePath(string configuredPath, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);

        var path = configuredPath.Trim();
        if (path.Equals("~", StringComparison.Ordinal))
        {
            return rootPath;
        }

        if (path.StartsWith("~/", StringComparison.Ordinal)
            || path.StartsWith(@"~\", StringComparison.Ordinal))
        {
            path = path[2..];
        }
        else if (path.StartsWith('~'))
        {
            throw new ArgumentException(
                "A virtual path must be either '~', '~/' or '~\\'.",
                nameof(configuredPath));
        }

        path = NormalizeDirectorySeparators(path);
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        var fullPath = Path.GetFullPath(path, rootPath);
        if (!IsPathWithinRoot(rootPath, fullPath))
        {
            throw new InvalidOperationException(RelativePathEscapesRootMessage);
        }

        return fullPath;
    }

    private static string ResolvePathWithinRoot(
        string path,
        string rootPath,
        string rootName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = ResolvePath(path, rootPath);
        if (!IsPathWithinRoot(rootPath, fullPath))
        {
            throw new InvalidOperationException(
                $"Resolved {rootName} path escapes the configured {rootName} root.");
        }

        return fullPath;
    }

    private static string NormalizeRootPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.GetFullPath(NormalizeDirectorySeparators(path));
    }

    private static string NormalizeDirectorySeparators(string path)
    {
        return path
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool IsPathWithinRoot(string rootPath, string path)
    {
        var relativePath = Path.GetRelativePath(
            NormalizeRootPath(rootPath),
            Path.GetFullPath(NormalizeDirectorySeparators(path)));

        return relativePath.Equals(".", StringComparison.Ordinal)
            || (!IsParentTraversal(relativePath) && !Path.IsPathRooted(relativePath));
    }

    private static bool IsParentTraversal(string relativePath)
    {
        return relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
