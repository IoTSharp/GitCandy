namespace GitCandy.Git;

/// <summary>
/// 一次 Git 仓库操作所需的路径上下文。
/// </summary>
/// <param name="RepositoryName">仓库名称。</param>
/// <param name="RepositoryPath">仓库在文件系统中的绝对路径。</param>
public sealed record GitRepositoryContext(string RepositoryName, string RepositoryPath);
