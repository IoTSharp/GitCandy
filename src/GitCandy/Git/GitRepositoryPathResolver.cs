using GitCandy.Configuration;

namespace GitCandy.Git;

/// <summary>
/// 基于 GitCandy 应用路径配置解析仓库文件系统路径。
/// </summary>
public sealed class GitRepositoryPathResolver(IGitCandyApplicationPaths applicationPaths)
    : IGitRepositoryPathResolver
{
    private static readonly char[] DirectorySeparators =
    [
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar
    ];

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly IGitCandyApplicationPaths _applicationPaths = applicationPaths;

    /// <inheritdoc />
    public string RepositoryRootPath => EnsureTrailingDirectorySeparator(
        Path.GetFullPath(_applicationPaths.RepositoryPath));

    /// <inheritdoc />
    public string ResolveRepositoryPath(string repositoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);

        var safeRepositoryName = repositoryName.Trim();
        if (safeRepositoryName.IndexOfAny(DirectorySeparators) >= 0)
        {
            throw new ArgumentException(
                "Repository name must be a single path segment.",
                nameof(repositoryName));
        }

        var rootPath = RepositoryRootPath;
        var repositoryPath = Path.GetFullPath(Path.Combine(rootPath, safeRepositoryName));
        if (!repositoryPath.StartsWith(rootPath, PathComparison))
        {
            throw new InvalidOperationException("Resolved repository path escapes the configured repository root.");
        }

        return repositoryPath;
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
