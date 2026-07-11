namespace GitCandy.PullRequests;

/// <summary>一次 Pull Request review 的决定。</summary>
public enum PullRequestReviewState
{
    Commented,
    Approved,
    ChangesRequested,
    Dismissed
}

/// <summary>Pull Request review 行为策略。</summary>
public sealed record PullRequestReviewPolicy(bool AllowAuthorApproval, bool DismissStaleApprovals);

/// <summary>可被指派为 assignee 或 reviewer 的仓库成员。</summary>
public sealed record PullRequestParticipant(string UserId, string UserName);

/// <summary>不可变 review 提交及其当前有效性。</summary>
public sealed record PullRequestReview(
    long Id,
    string ReviewerUserId,
    string Reviewer,
    PullRequestReviewState State,
    string BodyMarkdown,
    string BodyHtml,
    string HeadSha,
    bool IsStale,
    bool IsEffectiveApproval,
    string? DismissedBy,
    DateTime? DismissedAtUtc,
    string? DismissalReason,
    DateTime SubmittedAtUtc);

/// <summary>reviewer 请求和其最近一次 review 状态。</summary>
public sealed record PullRequestReviewerStatus(
    string UserId,
    string UserName,
    string RequestedBy,
    DateTime RequestedAtUtc,
    bool IsReviewRequested,
    PullRequestReview? LatestReview);

/// <summary>PR 的 assignee、reviewer 请求、review 历史和显式策略。</summary>
public sealed record PullRequestReviewOverview(
    string? AssigneeUserId,
    string? Assignee,
    IReadOnlyList<PullRequestParticipant> Candidates,
    IReadOnlyList<PullRequestReviewerStatus> Reviewers,
    IReadOnlyList<PullRequestReview> Reviews,
    PullRequestReviewPolicy Policy);

/// <summary>提交一次 comment、approve 或 request-changes review。</summary>
public sealed record SubmitPullRequestReviewCommand(PullRequestReviewState State, string Body);

/// <summary>行内评审锚点所在的 diff 一侧。</summary>
public enum PullRequestDiffSide
{
    Old,
    New
}

/// <summary>由 Git diff 验证并生成上下文的行内评审锚点。</summary>
public sealed record PullRequestReviewAnchor(string Path, PullRequestDiffSide Side, int StartLine, int EndLine, string Context);

/// <summary>创建行内评审 thread 的输入。</summary>
public sealed record CreatePullRequestReviewThreadCommand(string Path, PullRequestDiffSide Side, int StartLine, int EndLine, string Body);

/// <summary>行内评审回复。</summary>
public sealed record PullRequestReviewComment(long Id, string AuthorUserId, string Author, string BodyMarkdown, string BodyHtml, DateTime CreatedAtUtc);

/// <summary>可随新 head 重映射或明确过期的行内评审 thread。</summary>
public sealed record PullRequestReviewThread(
    long Id,
    string AuthorUserId,
    string OriginalBaseSha,
    string OriginalHeadSha,
    string OriginalPath,
    PullRequestDiffSide OriginalSide,
    int OriginalStartLine,
    int OriginalEndLine,
    string CurrentHeadSha,
    string? CurrentPath,
    PullRequestDiffSide? CurrentSide,
    int? CurrentStartLine,
    int? CurrentEndLine,
    bool IsOutdated,
    bool IsResolved,
    string? ResolvedBy,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<PullRequestReviewComment> Comments);
