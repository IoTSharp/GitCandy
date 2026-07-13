using GitCandy.Workspace;
using GitCandy.Notifications;

namespace GitCandy.Data.Domain;

public sealed class GitCandyTodo
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public long? RepositoryId { get; set; }
    public long? TeamId { get; set; }
    public WorkspaceResourceType ResourceType { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public WorkspaceTodoKind Kind { get; set; }
    public WorkspaceTodoStatus Status { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? SnoozedUntilUtc { get; set; }
    public long Version { get; set; }
}

public sealed class GitCandyNotification
{
    public long Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public long? RepositoryId { get; set; }
    public long? TeamId { get; set; }
    public WorkspaceNotificationEventType EventType { get; set; }
    public WorkspaceResourceType ResourceType { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public WorkspaceNotificationReason Reason { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public ICollection<GitCandyNotificationDelivery> Deliveries { get; } = [];
}

public sealed class GitCandyNotificationPreference
{
    public string UserId { get; set; } = string.Empty;
    public WorkspaceNotificationEventType EventType { get; set; }
    public bool EmailEnabled { get; set; }
    public bool WebhookEnabled { get; set; }
    public string? WebhookUrl { get; set; }
    public string? ProtectedWebhookSecret { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class GitCandyNotificationDelivery
{
    public string Id { get; set; } = string.Empty;
    public long NotificationId { get; set; }
    public NotificationDeliveryChannel Channel { get; set; }
    public NotificationDeliveryState State { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string? ProtectedSecret { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public GitCandyNotification? Notification { get; set; }
}

public sealed class GitCandyActivityEvent
{
    public string EventId { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string? ActorUserId { get; set; }
    public long? RepositoryId { get; set; }
    public long? TeamId { get; set; }
    public WorkspaceResourceType ResourceType { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    public WorkspaceActivityType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public DateTime RetainUntilUtc { get; set; }
}

public sealed class GitCandyRepositoryStar
{
    public string UserId { get; set; } = string.Empty;
    public long RepositoryId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class GitCandyRepositoryInteraction
{
    public string UserId { get; set; } = string.Empty;
    public long RepositoryId { get; set; }
    public DateTime LastInteractedAtUtc { get; set; }
    public int InteractionCount { get; set; }
}

public sealed class GitCandyRepositoryMetricDaily
{
    public long RepositoryId { get; set; }
    public DateTime DayUtc { get; set; }
    public int CommitCount { get; set; }
    public int ActiveCommitDays { get; set; }
    public int StarCount { get; set; }
    public int StarNetGrowth { get; set; }
    public long SuccessfulDownloadCount { get; set; }
    public long SuccessfulGitFetchCount { get; set; }
    public long UniquePageViewCount { get; set; }
    public string? LicenseSpdx { get; set; }
}

public sealed class GitCandyRepositoryPageView
{
    public long RepositoryId { get; set; }
    public DateTime DayUtc { get; set; }
    public string VisitorKey { get; set; } = string.Empty;
}

public sealed class GitCandyRepositoryRecommendationSnapshot
{
    public long Id { get; set; }
    public string SnapshotId { get; set; } = string.Empty;
    public long RepositoryId { get; set; }
    public string AlgorithmVersion { get; set; } = string.Empty;
    public DateTime CalculatedAtUtc { get; set; }
    public double CommitScore { get; set; }
    public double StarScore { get; set; }
    public double DownloadScore { get; set; }
    public double PageViewScore { get; set; }
    public double TotalScore { get; set; }
    public int Rank { get; set; }
    public string Explanation { get; set; } = string.Empty;
}
