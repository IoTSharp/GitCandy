namespace GitCandy.PullRequests;

/// <summary>Pull Request 当前状态。</summary>
public enum PullRequestState
{
    Open,
    Closed,
    Merged
}

/// <summary>Pull Request 支持的服务端合并方式。</summary>
public enum PullRequestMergeMethod
{
    MergeCommit,
    Squash
}

/// <summary>同步计算的 Pull Request 可合并状态。</summary>
public enum PullRequestMergeabilityState
{
    Mergeable,
    Draft,
    Closed,
    MissingSource,
    MissingTarget,
    SourceChanged,
    TargetChanged,
    Conflicting,
    ApprovalRequired,
    ChangesRequested,
    UnresolvedThreads,
    ChecksBlocked
}

/// <summary>Pull Request timeline 事件类型。</summary>
public enum PullRequestEventType
{
    Created,
    Edited,
    ConvertedToDraft,
    MarkedReady,
    Closed,
    Reopened,
    Merged,
    AssigneeChanged,
    ReviewRequested,
    ReviewRerequested,
    ReviewSubmitted,
    ReviewDismissed,
    MergeabilityRefreshed,
    MergeStarted,
    MergeFailed,
    IssuesClosed
}

/// <summary>Pull Request 分页筛选条件。</summary>
public sealed record PullRequestQuery(
    PullRequestState State = PullRequestState.Open,
    int Page = 1,
    int PageSize = 25);

/// <summary>创建同仓库 Pull Request 的输入。</summary>
public sealed record CreatePullRequestCommand(
    string Title,
    string Body,
    string AuthorUserId,
    string SourceBranch,
    string TargetBranch,
    bool IsDraft,
    long? SourceRepositoryId = null);

/// <summary>编辑 Pull Request 内容的输入。</summary>
public sealed record EditPullRequestCommand(string Title, string Body, long Version);

/// <summary>仓库分支及其当前 tip。</summary>
public sealed record PullRequestBranch(string Name, string CommitSha);

/// <summary>当前用户可作为 Pull Request source 的同 fork-network 仓库。</summary>
public sealed record PullRequestSourceRepository(
    long RepositoryId,
    string Namespace,
    string Repository,
    IReadOnlyList<PullRequestBranch> Branches);

/// <summary>source 与 target 分支的 Git 比较快照。</summary>
public sealed record PullRequestBranchComparison(
    string BaseSha,
    string HeadSha,
    int AheadBy,
    int BehindBy);

/// <summary>Pull Request 比较范围内的提交摘要。</summary>
public sealed record PullRequestCommit(
    string Sha,
    string Message,
    string MessageShort,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    IReadOnlyList<string> ParentShas);

/// <summary>Pull Request merge-base diff 中的单文件变更。</summary>
public sealed record PullRequestFileChange(
    string Path,
    string? OldPath,
    string Status,
    bool IsBinary,
    int LinesAdded,
    int LinesDeleted,
    string? Patch);

/// <summary>Pull Request 的 merge-base、提交页和可选文件 diff。</summary>
public sealed record PullRequestChangeSet(
    string MergeBaseSha,
    string BaseSha,
    string HeadSha,
    int AheadBy,
    int BehindBy,
    int CommitPage,
    int CommitPageSize,
    bool HasNextCommitPage,
    IReadOnlyList<PullRequestCommit> Commits,
    IReadOnlyList<PullRequestFileChange> Files,
    bool DiffTruncated);

/// <summary>Pull Request 列表摘要。</summary>
public sealed record PullRequestSummary(
    long Id,
    long Number,
    string Title,
    PullRequestState State,
    bool IsDraft,
    string Author,
    string SourceBranch,
    string TargetBranch,
    string CurrentHeadSha,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    long? SourceRepositoryId = null,
    string? SourceNamespace = null,
    string? SourceRepository = null);

/// <summary>Pull Request 分页结果。</summary>
public sealed record PullRequestPage(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<PullRequestSummary> Items);

/// <summary>Pull Request timeline 条目。</summary>
public sealed record PullRequestTimelineItem(
    long Id,
    PullRequestEventType Type,
    string? Actor,
    string? Detail,
    DateTime CreatedAtUtc);

/// <summary>Pull Request 详情与创建时 Git 快照。</summary>
public sealed record PullRequestDetails(
    long Id,
    long RepositoryId,
    long Number,
    string Title,
    string BodyMarkdown,
    string BodyHtml,
    PullRequestState State,
    bool IsDraft,
    string AuthorUserId,
    string Author,
    long? SourceRepositoryId,
    string SourceNamespace,
    string SourceRepository,
    string SourceBranch,
    string TargetBranch,
    string OriginalBaseSha,
    string OriginalHeadSha,
    string CurrentBaseSha,
    string CurrentHeadSha,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? MergedAtUtc,
    string? MergeCommitSha,
    long Version,
    IReadOnlyList<PullRequestTimelineItem> Timeline);

/// <summary>当前 head/base、review 和治理规则的可解释合并汇总。</summary>
public sealed record PullRequestMergeability(
    PullRequestMergeabilityState State,
    bool IsMergeable,
    string SourceSha,
    string TargetSha,
    bool HasConflicts,
    int EffectiveApprovals,
    int RequiredApprovals,
    int UnresolvedThreads,
    bool HasChangesRequested,
    bool RequiredChecksSatisfied,
    IReadOnlyList<string> BlockingReasons);

/// <summary>执行 merge commit 或 squash 的命令。</summary>
public sealed record MergePullRequestCommand(
    PullRequestMergeMethod Method,
    string ActorUserId,
    string ActorName,
    string ActorEmail,
    string Message,
    long Version);

/// <summary>应用服务执行合并的结果。</summary>
public sealed record PullRequestMergeResult(
    PullRequestMutationResult Result,
    string? CommitSha = null,
    int ClosedIssueCount = 0);

/// <summary>Git 层按不可变快照计算的冲突状态。</summary>
public sealed record PullRequestGitMergeability(
    bool SourceExists,
    bool TargetExists,
    bool SourceMatches,
    bool TargetMatches,
    bool HasConflicts,
    string? CurrentSourceSha,
    string? CurrentTargetSha);

/// <summary>Git 层创建提交并原子更新目标 ref 的结果。</summary>
public sealed record PullRequestGitMergeResult(
    PullRequestMutationResult Result,
    string? CommitSha = null);

/// <summary>受控 Web merge 进入 hook pipeline 前的不可变上下文。</summary>
public sealed record PullRequestMergeContext(
    long RepositoryId,
    long PullRequestNumber,
    string SourceBranch,
    string TargetBranch,
    string BaseSha,
    string HeadSha,
    PullRequestMergeMethod Method,
    string ActorUserId);

/// <summary>目标 ref 与数据库均提交成功后的 merge 事件。</summary>
public sealed record PullRequestMergedEvent(
    PullRequestMergeContext Context,
    string CommitSha,
    DateTimeOffset MergedAt);

/// <summary>Pull Request 修改结果。</summary>
public enum PullRequestMutationResult
{
    Succeeded,
    NotFound,
    Forbidden,
    Conflict,
    Invalid,
    Duplicate,
    NoChanges,
    BranchNotFound,
    HookRejected
}

/// <summary>Pull Request 输入不满足领域或 Git 快照约束。</summary>
public sealed class PullRequestValidationException(
    PullRequestMutationResult result,
    string message) : Exception(message)
{
    /// <summary>可映射到表单错误的失败类型。</summary>
    public PullRequestMutationResult Result { get; } = result;
}
