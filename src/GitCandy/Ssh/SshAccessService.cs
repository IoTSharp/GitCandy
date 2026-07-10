using System.Security.Cryptography;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Permissions;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Ssh;

/// <summary>
/// 通过短生命周期 DbContext 执行 SSH key 认证和仓库权限查询。
/// </summary>
public sealed class SshAccessService(IDbContextFactory<GitCandyDbContext> dbContextFactory)
    : ISshAccessService
{
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;

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
        if (key?.User.UserName is null || !MatchesStoredKey(key.PublicKey, publicKey))
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

        return new SshPrincipal(key.UserId, key.User.UserName, isAdministrator);
    }

    /// <inheritdoc />
    public async Task<bool> CanAccessRepositoryAsync(
        SshPrincipal principal,
        string repositoryName,
        bool requiresWrite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var permissionQuery = new GitCandyRepositoryPermissionQuery(dbContext);
        return requiresWrite
            ? await permissionQuery.CanWriteRepositoryAsync(
                repositoryName,
                principal.UserId,
                principal.IsAdministrator,
                cancellationToken)
            : await permissionQuery.CanReadRepositoryAsync(
                repositoryName,
                principal.UserId,
                principal.IsAdministrator,
                cancellationToken);
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
}
