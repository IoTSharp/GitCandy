using GitCandy.Governance;

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

    /// <summary>读取 assignee、reviewer 请求、review 历史和当前策略状态。</summary>
    Task<PullRequestReviewOverview?> GetReviewOverviewAsync(long repositoryId, long number, CancellationToken cancellationToken = default);

    /// <summary>设置独立于 author/reviewer 的 Pull Request assignee。</summary>
    Task<PullRequestMutationResult> SetAssigneeAsync(long repositoryId, long number, string actorUserId, bool isOwner, string? assigneeUserId, CancellationToken cancellationToken = default);

    /// <summary>请求或重新请求仓库成员 review。</summary>
    Task<PullRequestMutationResult> RequestReviewAsync(long repositoryId, long number, string actorUserId, bool isOwner, string reviewerUserId, CancellationToken cancellationToken = default);

    /// <summary>提交 comment、approve 或 request-changes review。</summary>
    Task<PullRequestMutationResult> SubmitReviewAsync(long repositoryId, long number, string reviewerUserId, SubmitPullRequestReviewCommand command, CancellationToken cancellationToken = default);

    /// <summary>由仓库 owner dismiss 一次 review，同时保留审计历史。</summary>
    Task<PullRequestMutationResult> DismissReviewAsync(long repositoryId, long number, long reviewId, string actorUserId, bool isOwner, string reason, CancellationToken cancellationToken = default);

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

    /// <summary>同步复核分支 tip、冲突、review、thread 和当前治理门禁。</summary>
    Task<PullRequestMergeability?> GetMergeabilityAsync(long repositoryId, long number, CancellationToken cancellationToken = default);

    /// <summary>在重新授权和重新计算 mergeability 后执行 merge commit 或 squash。</summary>
    Task<PullRequestMergeResult> MergePullRequestAsync(long repositoryId, long number, MergePullRequestCommand command, CancellationToken cancellationToken = default);

    /// <summary>读取可用于同仓库 Pull Request 的本地分支。</summary>
    Task<IReadOnlyList<PullRequestBranch>> GetBranchesAsync(
        long repositoryId,
        CancellationToken cancellationToken = default);

    /// <summary>读取当前用户可写且与 target 位于同一 fork network 的 source 仓库。</summary>
    Task<IReadOnlyList<PullRequestSourceRepository>> GetSourceRepositoriesAsync(
        long targetRepositoryId,
        string userId,
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

/// <summary>保护分支、审计、webhook 和索引入队共享的 Web merge 扩展点。</summary>
public interface IPullRequestMergeHook
{
    /// <summary>写入 Git ref 前验证；返回非成功结果会阻止合并。</summary>
    Task<PullRequestMutationResult> ValidateAsync(
        PullRequestMergeContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Git ref 与数据库成功提交后发布；失败不得回滚已完成的合并。</summary>
    Task OnMergedAsync(
        PullRequestMergedEvent mergedEvent,
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

    /// <summary>比较同一 fork network 内两个仓库的 source 与 target 分支。</summary>
    PullRequestBranchComparison? CompareBranches(
        string sourceRepositoryStorageName,
        string sourceBranch,
        string targetRepositoryStorageName,
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

    /// <summary>从目标 head 受限读取 CODEOWNERS，并返回 merge-base changed paths。</summary>
    CodeOwnersSnapshot ReadCodeOwnersSnapshot(
        string repositoryStorageName,
        string baseSha,
        string headSha,
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

    /// <summary>从 source 仓库导入已验证的 head，并更新 target 仓库只读 PR ref。</summary>
    void UpdatePullRequestHead(
        string sourceRepositoryStorageName,
        string sourceBranch,
        string targetRepositoryStorageName,
        long number,
        string expectedHeadSha,
        CancellationToken cancellationToken = default);

    /// <summary>基于 source/target 当前 ref 和保存快照同步计算冲突与 tip 变化。</summary>
    PullRequestGitMergeability EvaluateMergeability(
        string sourceRepositoryStorageName,
        string sourceBranch,
        string targetRepositoryStorageName,
        string targetBranch,
        long number,
        string expectedBaseSha,
        string expectedHeadSha,
        CancellationToken cancellationToken = default);

    /// <summary>在仓库级锁内复核 ref 后创建提交并更新 target branch。</summary>
    PullRequestGitMergeResult Merge(
        string sourceRepositoryStorageName,
        string sourceBranch,
        string targetRepositoryStorageName,
        string targetBranch,
        long number,
        string expectedBaseSha,
        string expectedHeadSha,
        PullRequestMergeMethod method,
        string message,
        string actorName,
        string actorEmail,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default);

    /// <summary>仅当 target 仍指向本次 merge commit 时补偿恢复旧 tip。</summary>
    bool RollbackMerge(
        string targetRepositoryStorageName,
        string targetBranch,
        string mergeCommitSha,
        string previousTargetSha,
        CancellationToken cancellationToken = default);

    /// <summary>在 PR 数据库事务失败时补偿删除尚未发布的内部 head ref。</summary>
    void DeletePullRequestHead(
        string repositoryStorageName,
        long number,
        CancellationToken cancellationToken = default);
}
