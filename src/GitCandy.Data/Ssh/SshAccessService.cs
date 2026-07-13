using System.Security.Cryptography;
using GitCandy.Application;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Permissions;
using GitCandy.Credentials;
using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Ssh;

/// <summary>
/// 通过短生命周期 DbContext 执行 SSH key 认证和仓库权限查询。
/// </summary>
internal sealed class SshAccessService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    TimeProvider timeProvider)
    : ISshAccessService
{
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<SshPrincipal?> AuthenticateAsync(
        string keyType,
        byte[] publicKey,
        bool recordUsage = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyType);
        ArgumentNullException.ThrowIfNull(publicKey);

        var fingerprint = Convert.ToBase64String(SHA256.HashData(publicKey)).TrimEnd('=');
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var userKey = await dbContext.SshKeys
            .Include(item => item.User)
            .SingleOrDefaultAsync(
                item => item.Fingerprint == fingerprint && item.KeyType == keyType,
                cancellationToken);
        if (userKey is not null && MatchesStoredKey(userKey.PublicKey, publicKey))
        {
            return (await CreateUserAuthorizedKeyAsync(
                dbContext,
                userKey,
                recordUsage,
                cancellationToken))?.Principal;
        }

        var deployKey = await dbContext.DeployKeys.SingleOrDefaultAsync(
            item => item.Fingerprint == fingerprint && item.KeyType == keyType,
            cancellationToken);
        return deployKey is null || !MatchesStoredKey(deployKey.PublicKey, publicKey)
            ? null
            : (await CreateDeployAuthorizedKeyAsync(dbContext, deployKey, recordUsage, cancellationToken))?.Principal;
    }

    /// <inheritdoc />
    public async Task<SshAuthorizedKey?> FindAuthorizedKeyAsync(
        string fingerprint,
        bool recordUsage = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        var normalizedFingerprint = NormalizeFingerprint(fingerprint);
        if (normalizedFingerprint is null)
        {
            return null;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var userKey = await dbContext.SshKeys
            .Include(item => item.User)
            .SingleOrDefaultAsync(
                item => item.Fingerprint == normalizedFingerprint,
                cancellationToken);
        if (userKey is not null)
        {
            return await CreateUserAuthorizedKeyAsync(dbContext, userKey, recordUsage, cancellationToken);
        }

        var deployKey = await dbContext.DeployKeys.SingleOrDefaultAsync(
            item => item.Fingerprint == normalizedFingerprint,
            cancellationToken);
        return deployKey is null
            ? null
            : await CreateDeployAuthorizedKeyAsync(dbContext, deployKey, recordUsage, cancellationToken);
    }

    private async Task<SshAuthorizedKey?> CreateDeployAuthorizedKeyAsync(
        GitCandyDbContext dbContext,
        GitCandyDeployKey key,
        bool recordUsage,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (key.RevokedAtUtc is not null
            || key.ExpiresAtUtc is DateTime expiresAtUtc && expiresAtUtc <= now.UtcDateTime)
        {
            return null;
        }

        if (recordUsage)
        {
            key.LastUsedAtUtc = now.UtcDateTime;
            dbContext.CredentialAuditEvents.Add(new GitCandyCredentialAuditEvent
            {
                CredentialKind = CredentialClaimTypes.DeployKey,
                CredentialId = key.Id,
                RepositoryId = key.RepositoryId,
                Action = "authenticate",
                Outcome = "success",
                Detail = key.CanWrite ? "write" : "read",
                OccurredAtUtc = now.UtcDateTime
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new SshAuthorizedKey(
            new SshPrincipal(
                UserId: null,
                UserName: $"deploy-key:{key.Name}",
                IsAdministrator: false,
                DeployKeyId: key.Id,
                RepositoryId: key.RepositoryId,
                CanWrite: key.CanWrite),
            key.KeyType,
            key.PublicKey,
            key.Fingerprint);
    }

    private async Task<SshAuthorizedKey?> CreateUserAuthorizedKeyAsync(
        GitCandyDbContext dbContext,
        GitCandySshKey key,
        bool recordUsage,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        var userName = key.User?.UserName;
        if (userName is null)
        {
            return null;
        }

        var userId = key.UserId;

        var normalizedAdministratorRole = RoleNames.Administrator.ToUpperInvariant();
        var isAdministrator = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == userId
                && role.NormalizedName == normalizedAdministratorRole
            select userRole)
            .AnyAsync(cancellationToken);

        if (recordUsage)
        {
            key.LastUsedAtUtc = now.UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new SshAuthorizedKey(
            new SshPrincipal(userId, userName, isAdministrator),
            key.KeyType,
            key.PublicKey,
            key.Fingerprint);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessRepositoryAsync(
        SshPrincipal principal,
        long repositoryId,
        bool requiresWrite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.DeployKeyId is not null)
        {
            return principal.RepositoryId == repositoryId
                && (!requiresWrite || principal.CanWrite);
        }

        if (principal.UserId is not string userId)
        {
            return false;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var permissionQuery = new GitCandyRepositoryPermissionQuery(dbContext);
        return requiresWrite
            ? await permissionQuery.CanWriteRepositoryAsync(
                repositoryId,
                userId,
                principal.IsAdministrator,
                cancellationToken)
            : await permissionQuery.CanReadRepositoryAsync(
                repositoryId,
                userId,
                principal.IsAdministrator,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RepositoryAddressResolution?> ResolveRepositoryAsync(
        string? namespaceSlug,
        string repositorySlug,
        bool legacy,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var resolver = new RepositoryAddressResolver(dbContext, _timeProvider);
        if (legacy || string.IsNullOrWhiteSpace(namespaceSlug))
        {
            return null;
        }

        var address = await resolver.ResolveAsync(namespaceSlug, repositorySlug, cancellationToken);
        return address?.UsedAlias == true ? null : address;
    }

    private static bool MatchesStoredKey(string storedPublicKey, byte[] publicKey)
    {
        try
        {
            var storedBytes = Convert.FromBase64String(storedPublicKey);
            return storedBytes.Length == publicKey.Length
                && CryptographicOperations.FixedTimeEquals(storedBytes, publicKey);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? NormalizeFingerprint(string fingerprint)
    {
        var normalized = fingerprint.Trim();
        const string openSshPrefix = "SHA256:";
        if (normalized.StartsWith(openSshPrefix, StringComparison.Ordinal))
        {
            normalized = normalized[openSshPrefix.Length..];
        }

        normalized = normalized.TrimEnd('=');
        if (normalized.Length != 43)
        {
            return null;
        }

        try
        {
            _ = Convert.FromBase64String(normalized + "=");
            return normalized;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
