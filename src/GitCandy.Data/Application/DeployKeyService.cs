using System.Security.Cryptography;
using GitCandy.Credentials;
using GitCandy.Data;
using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class DeployKeyService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    TimeProvider timeProvider) : IDeployKeyService
{
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<IReadOnlyList<DeployKeySummary>> GetForRepositoryAsync(
        long repositoryId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var keys = await dbContext.DeployKeys
            .AsNoTracking()
            .Where(key => key.RepositoryId == repositoryId)
            .OrderByDescending(key => key.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        return keys.Select(ToSummary).ToArray();
    }

    public async Task<DeployKeySummary?> CreateAsync(
        long repositoryId,
        string actorUserId,
        string name,
        string publicKey,
        bool canWrite,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);

        var normalizedName = name.Trim();
        var now = _timeProvider.GetUtcNow();
        if (normalizedName.Length > SchemaLimits.CredentialName
            || expiresAt <= now
            || !TryParsePublicKey(publicKey, out var parsedKey))
        {
            return null;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await dbContext.Repositories.AsNoTracking().AnyAsync(item => item.Id == repositoryId, cancellationToken)
            || await dbContext.SshKeys.AsNoTracking().AnyAsync(item => item.Fingerprint == parsedKey.Fingerprint, cancellationToken)
            || await dbContext.DeployKeys.AsNoTracking().AnyAsync(item => item.Fingerprint == parsedKey.Fingerprint, cancellationToken))
        {
            return null;
        }

        var key = new GitCandyDeployKey
        {
            RepositoryId = repositoryId,
            Name = normalizedName,
            KeyType = parsedKey.KeyType,
            PublicKey = parsedKey.PublicKey,
            Fingerprint = parsedKey.Fingerprint,
            CreatedAtUtc = now.UtcDateTime,
            CanWrite = canWrite,
            ExpiresAtUtc = expiresAt?.UtcDateTime,
            CreatedByUserId = actorUserId
        };
        dbContext.DeployKeys.Add(key);
        dbContext.SshFingerprintClaims.Add(new GitCandySshFingerprintClaim
        {
            Fingerprint = parsedKey.Fingerprint,
            CredentialKind = CredentialClaimTypes.DeployKey,
            ClaimedAtUtc = now.UtcDateTime
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        AddAudit(dbContext, key.Id, actorUserId, repositoryId, "create", "success", canWrite ? "write" : "read", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSummary(key);
    }

    public async Task<bool> RevokeAsync(
        long repositoryId,
        long keyId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        var now = _timeProvider.GetUtcNow();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var key = await dbContext.DeployKeys.SingleOrDefaultAsync(
            item => item.Id == keyId && item.RepositoryId == repositoryId,
            cancellationToken);
        if (key is null || key.RevokedAtUtc is not null)
        {
            return false;
        }

        key.RevokedAtUtc = now.UtcDateTime;
        AddAudit(dbContext, key.Id, actorUserId, repositoryId, "revoke", "success", string.Empty, now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool TryParsePublicKey(string publicKey, out ParsedSshKey parsedKey)
    {
        var parts = publicKey.Trim().Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2
            || !string.Equals(parts[0], "ssh-rsa", StringComparison.Ordinal)
            || parts[1].Length > SchemaLimits.SshPublicKey)
        {
            parsedKey = default;
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(parts[1]);
            parsedKey = new ParsedSshKey(
                parts[0],
                parts[1],
                Convert.ToBase64String(SHA256.HashData(bytes)).TrimEnd('='));
            return true;
        }
        catch (FormatException)
        {
            parsedKey = default;
            return false;
        }
    }

    private static DeployKeySummary ToSummary(GitCandyDeployKey key)
    {
        return new DeployKeySummary(
            key.Id,
            key.Name,
            key.KeyType,
            key.Fingerprint,
            key.CanWrite,
            ToDateTimeOffset(key.CreatedAtUtc),
            ToDateTimeOffset(key.ExpiresAtUtc),
            ToDateTimeOffset(key.LastUsedAtUtc),
            ToDateTimeOffset(key.RevokedAtUtc));
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value)
    {
        return value is null ? null : ToDateTimeOffset(value.Value);
    }

    private static void AddAudit(
        GitCandyDbContext dbContext,
        long credentialId,
        string actorUserId,
        long repositoryId,
        string action,
        string outcome,
        string detail,
        DateTimeOffset occurredAt)
    {
        dbContext.CredentialAuditEvents.Add(new GitCandyCredentialAuditEvent
        {
            CredentialKind = CredentialClaimTypes.DeployKey,
            CredentialId = credentialId,
            ActorUserId = actorUserId,
            RepositoryId = repositoryId,
            Action = action,
            Outcome = outcome,
            Detail = detail,
            OccurredAtUtc = occurredAt.UtcDateTime
        });
    }

    private readonly record struct ParsedSshKey(string KeyType, string PublicKey, string Fingerprint);
}
