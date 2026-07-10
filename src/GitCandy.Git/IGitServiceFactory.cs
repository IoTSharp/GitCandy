namespace GitCandy.Git;

/// <summary>
/// Git 仓库服务上下文工厂入口。
/// </summary>
public interface IGitServiceFactory
{
    /// <summary>
    /// 创建一次 Git 仓库操作所需的上下文。
    /// </summary>
    /// <param name="repositoryName">仓库名称。</param>
    /// <returns>Git 仓库操作上下文。</returns>
    GitRepositoryContext Create(string repositoryName);
}
