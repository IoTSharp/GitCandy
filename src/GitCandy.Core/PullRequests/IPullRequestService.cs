namespace GitCandy.PullRequests;

/// <summary>Pull Request 创建、查询和基础状态流转应用服务。</summary>
public interface IPullRequestService
{
    /// <summary>分页读取仓库 Pull Request。</summary>
    Task<PullRequestPage> GetPullRequestsAsync(
        long repositoryId,
        PullRequestQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>读取 Pull Request 详情与 timeline。</summary>
    Task<PullRequestDetails?> GetPullRequestAsync(
        long repositoryId,
        long number,
        CancellationToken cancellationToken = default);

    /// <summary>读取 PR 保存的 base/head 快照对应的提交页和 merge-base diff。</summary>
    Task<PullRequestChangeSet?> GetPullRequestChangesAsync(
        long repositoryId,
        long number,
        int commitPage,
        int commitPageSize,
        bool includeFiles,
        CancellationToken cancellationToken = default);

    /// <summary>读取可用于同仓库 Pull Request 的本地分支。</summary>
    Task<IReadOnlyList<PullRequestBranch>> GetBranchesAsync(
        long repositoryId,
        CancellationToken cancellationToken = default);

    /// <summary>创建 Pull Request 并维护服务端只读 head ref。</summary>
    Task<PullRequestDetails> CreatePullRequestAsync(
        long repositoryId,
        CreatePullRequestCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>以乐观并发版本编辑标题和描述。</summary>
    Task<PullRequestMutationResult> EditPullRequestAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        EditPullRequestCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>将打开的 Pull Request 转为 Draft 或 Ready。</summary>
    Task<PullRequestMutationResult> SetDraftAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        bool isDraft,
        CancellationToken cancellationToken = default);

    /// <summary>关闭或重新打开尚未合并的 Pull Request。</summary>
    Task<PullRequestMutationResult> SetStateAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        PullRequestState state,
        CancellationToken cancellationToken = default);
}

/// <summary>PR 应用服务使用的受控 Git 分支和内部 ref 能力。</summary>
public interface IPullRequestGitRepository
{
    /// <summary>读取仓库本地分支。</summary>
    IReadOnlyList<PullRequestBranch> GetBranches(
        string repositoryStorageName,
        CancellationToken cancellationToken = default);

    /// <summary>比较 target(base) 与 source(head) 分支。</summary>
    PullRequestBranchComparison? CompareBranches(
        string repositoryStorageName,
        string sourceBranch,
        string targetBranch,
        CancellationToken cancellationToken = default);

    /// <summary>按不可变 SHA 读取分页提交和 merge-base diff。</summary>
    PullRequestChangeSet? ReadChangeSet(
        string repositoryStorageName,
        string baseSha,
        string headSha,
        int commitPage,
        int commitPageSize,
        bool includeFiles,
        CancellationToken cancellationToken = default);

    /// <summary>创建或更新服务端维护的 refs/pull/{number}/head，并阻止 receive-pack 写入该命名空间。</summary>
    void UpdatePullRequestHead(
        string repositoryStorageName,
        long number,
        string headSha,
        CancellationToken cancellationToken = default);

    /// <summary>在 PR 数据库事务失败时补偿删除尚未发布的内部 head ref。</summary>
    void DeletePullRequestHead(
        string repositoryStorageName,
        long number,
        CancellationToken cancellationToken = default);
}
