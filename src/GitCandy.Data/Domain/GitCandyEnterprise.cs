using GitCandy.Enterprise;

namespace GitCandy.Data.Domain;

public sealed class GitCandyEnterpriseConnection
{
    public long Id { get; set; }
    public long TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public EnterpriseProviderKind Provider { get; set; }
    public string ExternalOrganizationId { get; set; } = string.Empty;
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? ConfigurationJson { get; set; }
    public string SecretReference { get; set; } = string.Empty;
    public string? WebhookSecretReference { get; set; }
    public string? SyncCursor { get; set; }
    public bool LoginEnabled { get; set; }
    public bool ProvisioningEnabled { get; set; }
    public bool IsEnabled { get; set; }
    public EnterpriseConnectionStatus Status { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTime? LastTestedAtUtc { get; set; }
    public DateTime? LastSynchronizedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public GitCandyTeam? Team { get; set; }
    public ICollection<GitCandyEnterpriseExternalIdentity> ExternalIdentities { get; } = [];
    public ICollection<GitCandyEnterpriseGroup> Groups { get; } = [];
    public GitCandyEnterpriseScimCredential? ScimCredential { get; set; }
    public ICollection<GitCandyEnterpriseProviderEvent> ProviderEvents { get; } = [];
}

public sealed class GitCandyEnterpriseScimCredential
{
    public long ConnectionId { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public GitCandyEnterpriseConnection? Connection { get; set; }
}

public sealed class GitCandyEnterpriseGroup
{
    public long Id { get; set; }
    public long ConnectionId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public GitCandyEnterpriseConnection? Connection { get; set; }
    public ICollection<GitCandyEnterpriseGroupMember> Members { get; } = [];
}

public sealed class GitCandyEnterpriseGroupMember
{
    public long GroupId { get; set; }
    public long ExternalIdentityId { get; set; }
    public GitCandyEnterpriseGroup? Group { get; set; }
    public GitCandyEnterpriseExternalIdentity? ExternalIdentity { get; set; }
}

public sealed class GitCandyEnterpriseProviderEvent
{
    public long Id { get; set; }
    public long ConnectionId { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public GitCandyEnterpriseConnection? Connection { get; set; }
}

public sealed class GitCandyEnterpriseExternalIdentity
{
    public long Id { get; set; }
    public long ConnectionId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; }
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime? DeprovisionedAtUtc { get; set; }
    public GitCandyEnterpriseConnection? Connection { get; set; }
}
