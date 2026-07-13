using GitCandy.Integrations;

namespace GitCandy.Data.Domain;

public sealed class GitCandyIntegrationEvent
{
    public string Id { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public long RepositoryId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public GitCandyRepository? Repository { get; set; }
    public ICollection<GitCandyWebhookDelivery> Deliveries { get; } = [];
}

public sealed class GitCandyWebhookSubscription
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string ProtectedSecret { get; set; } = string.Empty;
    public WebhookEventTypes Events { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public GitCandyRepository? Repository { get; set; }
    public ICollection<GitCandyWebhookDelivery> Deliveries { get; } = [];
}

public sealed class GitCandyWebhookDelivery
{
    public string Id { get; set; } = string.Empty;
    public long SubscriptionId { get; set; }
    public string EventId { get; set; } = string.Empty;
    public WebhookDeliveryState State { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ReplayOfDeliveryId { get; set; }
    public GitCandyWebhookSubscription? Subscription { get; set; }
    public GitCandyIntegrationEvent? Event { get; set; }
}

public sealed class GitCandyCommitCheck
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public string Sha { get; set; } = string.Empty;
    public CommitCheckKind Kind { get; set; }
    public string Context { get; set; } = string.Empty;
    public CommitCheckState State { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? TargetUrl { get; set; }
    public string? ExternalId { get; set; }
    public string ActorUserId { get; set; } = string.Empty;
    public long? CredentialId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public GitCandyRepository? Repository { get; set; }
}
