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

    /// <summary>读取 Pull Request 的行内评审 threads 与回复。</summary>
    Task<IReadOnlyList<PullRequestReviewThread>> GetReviewThreadsAsync(long repositoryId, long number, CancellationToken cancellationToken = default);

    /// <summary>创建由当前不可变 base/head diff 验证的行内评审 thread。</summary>
    Task<PullRequestMutationResult> AddReviewThreadAsync(long repositoryId, long number, string actorUserId, CreatePullRequestReviewThreadCommand command, CancellationToken cancellationToken = default);

    /// <summary>回复已有行内评审 thread。</summary>
    Task<PullRequestMutationResult> AddReviewReplyAsync(long repositoryId, long number, long threadId, string actorUserId, string body, CancellationToken cancellationToken = default);

    /// <summary>resolve 或重新打开行内评审 thread。</summary>
    Task<PullRequestMutationResult> SetReviewThreadResolvedAsync(long repositoryId, long number, long threadId, string actorUserId, bool isOwner, bool resolved, CancellationToken cancellationToken = default);

    /// <summary>刷新 source/target tip，并以保存的 hunk context 重映射 review anchors。</summary>
    Task<PullRequestMutationResult> RefreshPullRequestAsync(long repositoryId, long number, CancellationToken cancellationToken = default);

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

    /// <summary>验证行范围并从不可变 diff 生成可靠的 hunk context。</summary>
    PullRequestReviewAnchor? CaptureReviewAnchor(string repositoryStorageName, string baseSha, string headSha, string path, PullRequestDiffSide side, int startLine, int endLine, CancellationToken cancellationToken = default);

    /// <summary>在新 base/head diff 中唯一匹配保存的 hunk context；不唯一或不存在时返回 null。</summary>
    PullRequestReviewAnchor? RemapReviewAnchor(string repositoryStorageName, string baseSha, string headSha, PullRequestDiffSide side, string context, CancellationToken cancellationToken = default);

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
