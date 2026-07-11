using GitCandy.Data.Identity;
using GitCandy.Issues;

namespace GitCandy.Data.Domain;

public sealed class GitCandyWorkItemSequence
{
    public long RepositoryId { get; set; }
    public long NextNumber { get; set; } = 1;
    public long Version { get; set; }
    public GitCandyRepository? Repository { get; set; }
}

public sealed class GitCandyIssue
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public long Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string BodyMarkdown { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = string.Empty;
    public string? AssigneeUserId { get; set; }
    public long? MilestoneId { get; set; }
    public IssueState State { get; set; }
    public bool IsLocked { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public long Version { get; set; }
    public GitCandyRepository? Repository { get; set; }
    public GitCandyUser? Author { get; set; }
    public GitCandyUser? Assignee { get; set; }
    public GitCandyIssueMilestone? Milestone { get; set; }
    public ICollection<GitCandyIssueComment> Comments { get; } = [];
    public ICollection<GitCandyIssueTimelineEvent> Timeline { get; } = [];
    public ICollection<GitCandyIssueLabelLink> LabelLinks { get; } = [];
    public ICollection<GitCandyIssueSubscription> Subscriptions { get; } = [];
}

public sealed class GitCandyIssueComment
{
    public long Id { get; set; }
    public long IssueId { get; set; }
    public string AuthorUserId { get; set; } = string.Empty;
    public string BodyMarkdown { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? EditedAtUtc { get; set; }
    public bool IsHidden { get; set; }
    public string? HiddenByUserId { get; set; }
    public DateTime? HiddenAtUtc { get; set; }
    public long Version { get; set; }
    public GitCandyIssue? Issue { get; set; }
    public GitCandyUser? Author { get; set; }
}

public sealed class GitCandyIssueEdit
{
    public long Id { get; set; }
    public long IssueId { get; set; }
    public long? CommentId { get; set; }
    public string EditorUserId { get; set; } = string.Empty;
    public string PreviousMarkdown { get; set; } = string.Empty;
    public string PreviousHtml { get; set; } = string.Empty;
    public DateTime EditedAtUtc { get; set; }
}

public sealed class GitCandyIssueTimelineEvent
{
    public long Id { get; set; }
    public long IssueId { get; set; }
    public string? ActorUserId { get; set; }
    public IssueEventType Type { get; set; }
    public long? CommentId { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public GitCandyIssue? Issue { get; set; }
    public GitCandyIssueComment? Comment { get; set; }
    public GitCandyUser? Actor { get; set; }
}

public sealed class GitCandyIssueLabel
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Color { get; set; } = "6e7781";
    public string Description { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public GitCandyRepository? Repository { get; set; }
    public ICollection<GitCandyIssueLabelLink> IssueLinks { get; } = [];
}

public sealed class GitCandyIssueLabelLink
{
    public long IssueId { get; set; }
    public long LabelId { get; set; }
    public GitCandyIssue? Issue { get; set; }
    public GitCandyIssueLabel? Label { get; set; }
}

public sealed class GitCandyIssueMilestone
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? DueAtUtc { get; set; }
    public bool IsClosed { get; set; }
    public bool IsArchived { get; set; }
    public GitCandyRepository? Repository { get; set; }
    public ICollection<GitCandyIssue> Issues { get; } = [];
}

public sealed class GitCandyIssueSubscription
{
    public long IssueId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public bool IsSubscribed { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public GitCandyIssue? Issue { get; set; }
    public GitCandyUser? User { get; set; }
}

public sealed class GitCandyIssueNotification
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public long RepositoryId { get; set; }
    public long IssueId { get; set; }
    public string? ActorUserId { get; set; }
    public IssueNotificationType Type { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

public sealed class GitCandyIssueRelation
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public long SourceIssueId { get; set; }
    public long TargetIssueId { get; set; }
    public IssueRelationType Type { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class GitCandyIssueReference
{
    public long Id { get; set; }
    public long SourceIssueId { get; set; }
    public long? TargetRepositoryId { get; set; }
    public long? TargetIssueId { get; set; }
    public string? CommitSha { get; set; }
    public string DisplayText { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
