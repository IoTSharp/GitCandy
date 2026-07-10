using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Permissions;

/// <summary>
/// 基于 EF Core 领域表的仓库权限查询。
/// </summary>
public sealed class GitCandyRepositoryPermissionQuery(GitCandyDbContext dbContext)
    : IGitCandyRepositoryPermissionQuery
{
    private readonly GitCandyDbContext _dbContext = dbContext;

    /// <inheritdoc />
    public Task<bool> CanReadRepositoryAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        return CanAccessRepositoryAsync(repositoryName, userId, isAdministrator, requiresWrite: false, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CanWriteRepositoryAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        return CanAccessRepositoryAsync(repositoryName, userId, isAdministrator, requiresWrite: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsRepositoryOwnerAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(repositoryName);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        if (isAdministrator)
        {
            return await _dbContext.Repositories
                .AsNoTracking()
                .AnyAsync(repository => repository.NormalizedName == normalizedName, cancellationToken);
        }

        return await (
            from role in _dbContext.UserRepositoryRoles.AsNoTracking()
            join repository in _dbContext.Repositories.AsNoTracking()
                on role.RepositoryId equals repository.Id
            where repository.NormalizedName == normalizedName
                && role.UserId == userId
                && role.IsOwner
            select role)
            .AnyAsync(cancellationToken);
    }

    private async Task<bool> CanAccessRepositoryAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        bool requiresWrite,
        CancellationToken cancellationToken)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(repositoryName);
        var repositories = _dbContext.Repositories
            .AsNoTracking()
            .Where(repository => repository.NormalizedName == normalizedName);

        if (isAdministrator && !string.IsNullOrWhiteSpace(userId))
        {
            return await repositories.AnyAsync(cancellationToken);
        }

        if (requiresWrite)
        {
            if (await repositories.AnyAsync(
                repository => !repository.IsPrivate
                    && repository.AllowAnonymousRead
                    && repository.AllowAnonymousWrite,
                cancellationToken))
            {
                return true;
            }
        }
        else if (await repositories.AnyAsync(
            repository => !repository.IsPrivate && repository.AllowAnonymousRead,
            cancellationToken))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var directRoleExists = await (
            from role in _dbContext.UserRepositoryRoles.AsNoTracking()
            join repository in _dbContext.Repositories.AsNoTracking()
                on role.RepositoryId equals repository.Id
            where repository.NormalizedName == normalizedName
                && role.UserId == userId
                && role.AllowRead
                && (!requiresWrite || role.AllowWrite)
            select role)
            .AnyAsync(cancellationToken);

        if (directRoleExists)
        {
            return true;
        }

        return await (
            from teamRole in _dbContext.TeamRepositoryRoles.AsNoTracking()
            join repository in _dbContext.Repositories.AsNoTracking()
                on teamRole.RepositoryId equals repository.Id
            join userTeamRole in _dbContext.UserTeamRoles.AsNoTracking()
                on teamRole.TeamId equals userTeamRole.TeamId
            where repository.NormalizedName == normalizedName
                && userTeamRole.UserId == userId
                && teamRole.AllowRead
                && (!requiresWrite || teamRole.AllowWrite)
            select teamRole)
            .AnyAsync(cancellationToken);
    }
}
