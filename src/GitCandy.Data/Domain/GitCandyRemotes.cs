using GitCandy.Data.Identity;
using GitCandy.Remotes;

namespace GitCandy.Data.Domain;

public sealed class GitCandyRemoteAccountConnection
{
    public long Id { get; set; }
    public RemoteConnectionOwnerKind OwnerKind { get; set; }
    public string? OwnerUserId { get; set; }
    public long? OwnerTeamId { get; set; }
    public RemoteProviderKind Provider { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public string ExternalAccountId { get; set; } = string.Empty;
    public RemoteAccountKind AccountKind { get; set; }
    public string Login { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public RemoteAuthenticationKind AuthenticationKind { get; set; }
    public string CredentialReference { get; set; } = string.Empty;
    public DateTime? CredentialExpiresAtUtc { get; set; }
    public string? WebhookSecretReference { get; set; }
    public string GrantedScopes { get; set; } = "[]";
    public bool IsEnabled { get; set; }
    public RemoteConnectionStatus Status { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTime? LastTestedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public GitCandyUser? OwnerUser { get; set; }
    public GitCandyTeam? OwnerTeam { get; set; }
    public ICollection<GitCandyRepositoryMirror> Mirrors { get; } = [];
    public ICollection<GitCandyRemoteProviderEvent> ProviderEvents { get; } = [];
}

public sealed class GitCandyRepositoryMirror
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public long ConnectionId { get; set; }
    public string RemoteRepositoryId { get; set; } = string.Empty;
    public string RemoteOwnerLogin { get; set; } = string.Empty;
    public string RemoteRepositoryName { get; set; } = string.Empty;
    public string RemoteGitUrl { get; set; } = string.Empty;
    public RemoteMirrorDirection Direction { get; set; }
    public RemoteMirrorAuthority Authority { get; set; }
    public RemoteMirrorRefFilterKind RefFilterKind { get; set; }
    public string? RefFilterPattern { get; set; }
    public int? ScheduleIntervalMinutes { get; set; }
    public string? ScheduleTimeZone { get; set; }
    public bool ScheduleEnabled { get; set; }
    public RemoteMirrorDivergencePolicy DivergencePolicy { get; set; }
    public bool Prune { get; set; }
    public bool IsEnabled { get; set; }
    public RemoteMirrorStatus Status { get; set; }
    public string? LastObservedRemoteHead { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTime? LastAttemptedAtUtc { get; set; }
    public DateTime? LastSucceededAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public GitCandyRepository? Repository { get; set; }
    public GitCandyRemoteAccountConnection? Connection { get; set; }
    public ICollection<GitCandyRemoteMirrorRefUpdate> PendingRefUpdates { get; } = [];
    public GitCandyRemoteMirrorJob? Job { get; set; }
}

public sealed class GitCandyRemoteMirrorRefUpdate
{
    public long MirrorId { get; set; }
    public string ReferenceName { get; set; } = string.Empty;
    public string OldObjectId { get; set; } = string.Empty;
    public string NewObjectId { get; set; } = string.Empty;
    public long Generation { get; set; }
    public DateTime EnqueuedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public GitCandyRepositoryMirror? Mirror { get; set; }
}

public sealed class GitCandyRemoteMirrorJob
{
    public long Id { get; set; }
    public long MirrorId { get; set; }
    public RemoteMirrorJobState State { get; set; }
    public RemoteMirrorJobTrigger Triggers { get; set; }
    public long RequestedGeneration { get; set; }
    public long ProcessedGeneration { get; set; }
    public int AttemptCount { get; set; }
    public DateTime AvailableAtUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? CancellationRequestedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTime? LastStartedAtUtc { get; set; }
    public DateTime? LastCompletedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long Version { get; set; }
    public GitCandyRepositoryMirror? Mirror { get; set; }
}

public sealed class GitCandyRemoteProviderEvent
{
    public long Id { get; set; }
    public long ConnectionId { get; set; }
    public RemoteProviderKind Provider { get; set; }
    public string DeliveryId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public GitCandyRemoteAccountConnection? Connection { get; set; }
}
