namespace GitCandy.Credentials;

/// <summary>仓库级 deploy key 的脱敏摘要。</summary>
public sealed record DeployKeySummary(
    long Id,
    string Name,
    string KeyType,
    string Fingerprint,
    bool CanWrite,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

/// <summary>仓库机器 SSH 凭据的管理边界。</summary>
public interface IDeployKeyService
{
    Task<IReadOnlyList<DeployKeySummary>> GetForRepositoryAsync(
        long repositoryId,
        CancellationToken cancellationToken = default);

    Task<DeployKeySummary?> CreateAsync(
        long repositoryId,
        string actorUserId,
        string name,
        string publicKey,
        bool canWrite,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        long repositoryId,
        long keyId,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
