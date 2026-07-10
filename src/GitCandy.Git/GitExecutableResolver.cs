using GitCandy.Configuration;

namespace GitCandy.Git;

/// <summary>
/// 基于应用路径配置解析 Git 可执行文件。
/// </summary>
public sealed class GitExecutableResolver(IGitCandyApplicationPaths applicationPaths)
    : IGitExecutableResolver
{
    private readonly IGitCandyApplicationPaths _applicationPaths = applicationPaths;

    /// <inheritdoc />
    public string Resolve()
    {
        if (string.IsNullOrWhiteSpace(_applicationPaths.GitCorePath))
        {
            return OperatingSystem.IsWindows() ? "git.exe" : "git";
        }

        var fileNames = OperatingSystem.IsWindows()
            ? new[] { "git.exe", "git" }
            : new[] { "git", "git.exe" };
        foreach (var fileName in fileNames)
        {
            var candidate = Path.Combine(_applicationPaths.GitCorePath, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new GitTransportException(
            "GitCorePath does not contain a Git executable.");
    }
}
