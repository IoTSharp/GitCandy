namespace GitCandy.Git;

/// <summary>
/// Git 仓库路径解析入口。
/// </summary>
public interface IGitRepositoryPathResolver
{
    /// <summary>
    /// 仓库存储根目录的绝对路径。
    /// </summary>
    string RepositoryRootPath { get; }

    /// <summary>
    /// 将仓库名称解析为仓库绝对路径，并确保结果位于仓库存储根目录下。
    /// </summary>
    /// <param name="repositoryName">仓库名称。</param>
    /// <returns>仓库绝对路径。</returns>
    string ResolveRepositoryPath(string repositoryName);
}
