using GitCandy.Configuration;

namespace GitCandy.Git;

/// <summary>
/// 基于 GitCandy 应用路径配置解析仓库文件系统路径。
/// </summary>
public sealed class GitRepositoryPathResolver(IGitCandyApplicationPaths applicationPaths)
    : IGitRepositoryPathResolver
{
    private static readonly char[] DirectorySeparators = ['/', '\\'];

    private readonly IGitCandyApplicationPaths _applicationPaths = applicationPaths;

    /// <inheritdoc />
    public string RepositoryRootPath => EnsureTrailingDirectorySeparator(
        Path.GetFullPath(_applicationPaths.RepositoryPath));

    /// <inheritdoc />
    public string ResolveRepositoryPath(string repositoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);

        var safeRepositoryName = repositoryName.Trim();
        if (safeRepositoryName is "." or ".."
            || safeRepositoryName.IndexOfAny(DirectorySeparators) >= 0)
        {
            throw new ArgumentException(
                "Repository name must be a single path segment.",
                nameof(repositoryName));
        }

        return _applicationPaths.ResolvePathWithinRepositoryRoot(safeRepositoryName);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return Path.EndsInDirectorySeparator(path)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
