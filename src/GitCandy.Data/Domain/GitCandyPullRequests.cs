using GitCandy.Data.Identity;
using GitCandy.PullRequests;

namespace GitCandy.Data.Domain;

public sealed class GitCandyPullRequest
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public long? SourceRepositoryId { get; set; }
    public string SourceNamespaceSnapshot { get; set; } = string.Empty;
    public string SourceRepositorySnapshot { get; set; } = string.Empty;
    public long Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string BodyMarkdown { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public string? AssigneeUserId { get; set; }
    public string SourceBranch { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = string.Empty;
    public string OriginalBaseSha { get; set; } = string.Empty;
    public string OriginalHeadSha { get; set; } = string.Empty;
    public string CurrentBaseSha { get; set; } = string.Empty;
    public string CurrentHeadSha { get; set; } = string.Empty;
    public PullRequestState State { get; set; }
    public bool IsDraft { get; set; }
    public string ActivePairKey { get; set; } = string.Empty;
    public string? MergeCommitSha { get; set; }
    public string? MergedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime? MergedAtUtc { get; set; }
    public long Version { get; set; }
    public GitCandyRepository? Repository { get; set; }
    public GitCandyRepository? SourceRepository { get; set; }
    public GitCandyUser? Author { get; set; }
    public GitCandyUser? Assignee { get; set; }
    public GitCandyUser? MergedBy { get; set; }
    public ICollection<GitCandyPullRequestTimelineEvent> Timeline { get; } = [];
    public ICollection<GitCandyPullRequestReviewThread> ReviewThreads { get; } = [];
    public ICollection<GitCandyPullRequestReviewer> Reviewers { get; } = [];
    public ICollection<GitCandyPullRequestReview> Reviews { get; } = [];
}

public sealed class GitCandyPullRequestTimelineEvent
{
    public long Id { get; set; }
    public long PullRequestId { get; set; }
    public string? ActorUserId { get; set; }
    public PullRequestEventType Type { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public GitCandyPullRequest? PullRequest { get; set; }
    public GitCandyUser? Actor { get; set; }
}
