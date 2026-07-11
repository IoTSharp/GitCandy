namespace GitCandy.PullRequests;

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
