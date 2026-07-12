namespace GitCandy.Git;

/// <summary>
/// 使用托管仓库能力执行非 Git wire protocol 操作。
/// </summary>
public interface IManagedGitRepositoryService
{
    /// <summary>
    /// 解析并验证位于配置根目录内的现有 Git 仓库。
    /// </summary>
    /// <param name="repository">仓库上下文。</param>
    /// <returns>通过路径边界和 Git 仓库格式验证的绝对路径。</returns>
    string ResolveExistingPath(GitRepositoryContext repository);

    /// <summary>
    /// 在配置的仓库根目录中初始化 bare Git 仓库。
    /// </summary>
    /// <param name="repositoryName">单段仓库名称，不包含 <c>.git</c> 后缀。</param>
    /// <returns>新仓库上下文。</returns>
    GitRepositoryContext InitializeBare(string repositoryName);

    /// <summary>
    /// 将远程或本地 Git 仓库以 bare 形式克隆到配置根目录。
    /// </summary>
    GitRepositoryContext CloneBare(
        string source,
        string repositoryName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置 bare 仓库 HEAD 指向的本地分支。
    /// </summary>
    bool SetDefaultBranch(
        GitRepositoryContext repository,
        string branchName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取仓库 HEAD、分支、标签和最近提交摘要。
    /// </summary>
    /// <param name="repository">仓库上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>仓库快照。</returns>
    GitRepositorySnapshot ReadSnapshot(
        GitRepositoryContext repository,
        CancellationToken cancellationToken = default);

    /// <summary>删除非默认本地分支。</summary>
    bool DeleteBranch(GitRepositoryContext repository, string branchName, CancellationToken cancellationToken = default);

    /// <summary>删除本地 tag。</summary>
    bool DeleteTag(GitRepositoryContext repository, string tagName, CancellationToken cancellationToken = default);
}
