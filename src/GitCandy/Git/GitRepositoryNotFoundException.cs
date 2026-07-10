namespace GitCandy.Git;

/// <summary>
/// Git transport 请求对应的物理仓库不存在。
/// </summary>
public sealed class GitRepositoryNotFoundException : Exception
{
    /// <summary>
    /// 使用仓库名称创建异常。
    /// </summary>
    /// <param name="repositoryName">仓库名称。</param>
    public GitRepositoryNotFoundException(string repositoryName)
        : base($"Git repository '{repositoryName}' was not found.")
    {
    }
}
