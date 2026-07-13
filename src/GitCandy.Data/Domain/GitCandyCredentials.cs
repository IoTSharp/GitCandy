using GitCandy.Data.Identity;

namespace GitCandy.Data.Domain;

/// <summary>只保存不可逆 hash 的 Personal Access Token。</summary>
public sealed class GitCandyPersonalAccessToken
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string TokenPrefix { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public GitCandyUser? User { get; set; }
}

/// <summary>凭据生命周期和使用结果的脱敏审计事件。</summary>
public sealed class GitCandyCredentialAuditEvent
{
    public long Id { get; set; }
    public string CredentialKind { get; set; } = string.Empty;
    public long CredentialId { get; set; }
    public string? ActorUserId { get; set; }
    public long? RepositoryId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>仓库级 SSH deploy key；不关联 Identity 登录身份。</summary>
public sealed class GitCandyDeployKey
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyType { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public bool CanWrite { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? LastUsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public GitCandyRepository? Repository { get; set; }
}

/// <summary>新建 SSH key 的跨类型全局 fingerprint 占位。</summary>
public sealed class GitCandySshFingerprintClaim
{
    public string Fingerprint { get; set; } = string.Empty;
    public string CredentialKind { get; set; } = string.Empty;
    public DateTime ClaimedAtUtc { get; set; }
}
