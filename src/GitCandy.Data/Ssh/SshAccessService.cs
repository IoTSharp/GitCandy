using System.Security.Cryptography;
using GitCandy.Application;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Permissions;
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
        var key = await dbContext.SshKeys
            .Include(item => item.User)
            .SingleOrDefaultAsync(
                item => item.Fingerprint == fingerprint && item.KeyType == keyType,
                cancellationToken);
        if (key is null || !MatchesStoredKey(key.PublicKey, publicKey))
        {
            return null;
        }

        return (await CreateAuthorizedKeyAsync(
            dbContext,
            key,
            recordUsage,
            cancellationToken))?.Principal;
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
        var key = await dbContext.SshKeys
            .Include(item => item.User)
            .SingleOrDefaultAsync(
                item => item.Fingerprint == normalizedFingerprint,
                cancellationToken);
        return key is null
            ? null
            : await CreateAuthorizedKeyAsync(dbContext, key, recordUsage, cancellationToken);
    }

    private static async Task<SshAuthorizedKey?> CreateAuthorizedKeyAsync(
        GitCandyDbContext dbContext,
        GitCandy.Data.Domain.GitCandySshKey key,
        bool recordUsage,
        CancellationToken cancellationToken)
    {
        var userName = key.User?.UserName;
        if (userName is null)
        {
            return null;
        }

        var normalizedAdministratorRole = RoleNames.Administrator.ToUpperInvariant();
        var isAdministrator = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == key.UserId
                && role.NormalizedName == normalizedAdministratorRole
            select userRole)
            .AnyAsync(cancellationToken);

        if (recordUsage)
        {
            key.LastUsedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new SshAuthorizedKey(
            new SshPrincipal(key.UserId, userName, isAdministrator),
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
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var permissionQuery = new GitCandyRepositoryPermissionQuery(dbContext);
        return requiresWrite
            ? await permissionQuery.CanWriteRepositoryAsync(
                repositoryId,
                principal.UserId,
                principal.IsAdministrator,
                cancellationToken)
            : await permissionQuery.CanReadRepositoryAsync(
                repositoryId,
                principal.UserId,
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
        return legacy
            ? await resolver.ResolveLegacyAsync(repositorySlug, cancellationToken)
            : await resolver.ResolveAsync(namespaceSlug!, repositorySlug, cancellationToken);
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
