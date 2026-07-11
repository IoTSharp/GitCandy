namespace GitCandy.Application;

/// <summary>
/// 协调仓库元数据与物理 Git 仓库的生命周期操作。
/// </summary>
public interface IRepositoryLifecycleService
{
    /// <summary>创建空仓库、导入远程仓库或从现有仓库 fork。</summary>
    Task<RepositoryLifecycleResult> CreateAsync(
        RepositoryCreation request,
        CancellationToken cancellationToken = default);

    /// <summary>设置 bare 仓库 HEAD 指向的默认分支。</summary>
    Task<bool> SetDefaultBranchAsync(
        string repositoryName,
        string branchName,
        CancellationToken cancellationToken = default);

    /// <summary>安全删除物理仓库、LFS 对象和元数据。</summary>
    Task<bool> DeleteAsync(
        string repositoryName,
        CancellationToken cancellationToken = default);
}

/// <summary>仓库初始化方式。</summary>
public enum RepositoryCreationMode
{
    Empty,
    Import,
    Fork
}

/// <summary>仓库创建请求。</summary>
public sealed record RepositoryCreation(
    RepositoryEdit Repository,
    string CreatorUserId,
    RepositoryCreationMode Mode = RepositoryCreationMode.Empty,
    string? Source = null);

/// <summary>仓库生命周期操作结果。</summary>
public sealed record RepositoryLifecycleResult(bool Succeeded, string? Error = null);
