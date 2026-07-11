namespace GitCandy.PullRequests;

/// <summary>Pull Request 当前状态。</summary>
public enum PullRequestState
{
    Open,
    Closed,
    Merged
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
    ReviewDismissed
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
    bool IsDraft);

/// <summary>编辑 Pull Request 内容的输入。</summary>
public sealed record EditPullRequestCommand(string Title, string Body, long Version);

/// <summary>仓库分支及其当前 tip。</summary>
public sealed record PullRequestBranch(string Name, string CommitSha);

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
    DateTime UpdatedAtUtc);

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
    BranchNotFound
}

/// <summary>Pull Request 输入不满足领域或 Git 快照约束。</summary>
public sealed class PullRequestValidationException(
    PullRequestMutationResult result,
    string message) : Exception(message)
{
    /// <summary>可映射到表单错误的失败类型。</summary>
    public PullRequestMutationResult Result { get; } = result;
}
