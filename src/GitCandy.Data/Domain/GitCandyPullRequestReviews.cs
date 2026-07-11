using GitCandy.Data.Identity;
using GitCandy.PullRequests;

namespace GitCandy.Data.Domain;

public sealed class GitCandyPullRequestReviewer
{
    public long PullRequestId { get; set; }
    public string ReviewerUserId { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public long Version { get; set; }
    public GitCandyPullRequest? PullRequest { get; set; }
    public GitCandyUser? Reviewer { get; set; }
    public GitCandyUser? RequestedBy { get; set; }
}

public sealed class GitCandyPullRequestReview
{
    public long Id { get; set; }
    public long PullRequestId { get; set; }
    public string ReviewerUserId { get; set; } = string.Empty;
    public PullRequestReviewState State { get; set; }
    public string BodyMarkdown { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string HeadSha { get; set; } = string.Empty;
    public long ReviewerRequestVersion { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public string? DismissedByUserId { get; set; }
    public DateTime? DismissedAtUtc { get; set; }
    public string? DismissalReason { get; set; }
    public long Version { get; set; }
    public GitCandyPullRequest? PullRequest { get; set; }
    public GitCandyUser? Reviewer { get; set; }
    public GitCandyUser? DismissedBy { get; set; }
}

public sealed class GitCandyPullRequestReviewThread
{
    public long Id { get; set; }
    public long PullRequestId { get; set; }
    public string AuthorUserId { get; set; } = string.Empty;
    public string OriginalBaseSha { get; set; } = string.Empty;
    public string OriginalHeadSha { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    public PullRequestDiffSide OriginalSide { get; set; }
    public int OriginalStartLine { get; set; }
    public int OriginalEndLine { get; set; }
    public string AnchorContext { get; set; } = string.Empty;
    public string CurrentHeadSha { get; set; } = string.Empty;
    public string? CurrentPath { get; set; }
    public PullRequestDiffSide? CurrentSide { get; set; }
    public int? CurrentStartLine { get; set; }
    public int? CurrentEndLine { get; set; }
    public bool IsOutdated { get; set; }
    public bool IsResolved { get; set; }
    public string? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
    public GitCandyPullRequest? PullRequest { get; set; }
    public GitCandyUser? Author { get; set; }
    public GitCandyUser? ResolvedBy { get; set; }
    public ICollection<GitCandyPullRequestReviewComment> Comments { get; } = [];
}

public sealed class GitCandyPullRequestReviewComment
{
    public long Id { get; set; }
    public long ThreadId { get; set; }
    public string AuthorUserId { get; set; } = string.Empty;
    public string BodyMarkdown { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public long Version { get; set; }
    public GitCandyPullRequestReviewThread? Thread { get; set; }
    public GitCandyUser? Author { get; set; }
}
