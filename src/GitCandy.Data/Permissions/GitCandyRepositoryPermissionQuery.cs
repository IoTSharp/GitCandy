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
        return CanAccessRepositoryAsync(repositoryName, null, userId, isAdministrator, requiresWrite: false, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CanReadRepositoryAsync(
        long repositoryId,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        return CanAccessRepositoryAsync(null, repositoryId, userId, isAdministrator, requiresWrite: false, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CanWriteRepositoryAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        return CanAccessRepositoryAsync(repositoryName, null, userId, isAdministrator, requiresWrite: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CanWriteRepositoryAsync(
        long repositoryId,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        return CanAccessRepositoryAsync(null, repositoryId, userId, isAdministrator, requiresWrite: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsRepositoryOwnerAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        if (isAdministrator)
        {
            return await SelectRepositories(repositoryName, null).AnyAsync(cancellationToken);
        }

        return await (
            from role in _dbContext.UserRepositoryRoles.AsNoTracking()
            join repository in SelectRepositories(repositoryName, null)
                on role.RepositoryId equals repository.Id
            where role.UserId == userId
                && role.IsOwner
            select role)
            .AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsRepositoryOwnerAsync(
        long repositoryId,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        if (isAdministrator)
        {
            return await _dbContext.Repositories.AsNoTracking()
                .AnyAsync(repository => repository.Id == repositoryId, cancellationToken);
        }

        return await _dbContext.UserRepositoryRoles.AsNoTracking().AnyAsync(
            role => role.RepositoryId == repositoryId && role.UserId == userId && role.IsOwner,
            cancellationToken);
    }

    private async Task<bool> CanAccessRepositoryAsync(
        string? repositoryName,
        long? repositoryId,
        string? userId,
        bool isAdministrator,
        bool requiresWrite,
        CancellationToken cancellationToken)
    {
        var repositories = SelectRepositories(repositoryName, repositoryId);

        if (requiresWrite && await repositories.AnyAsync(repository =>
                _dbContext.RepositoryMirrors.Any(mirror =>
                    mirror.RepositoryId == repository.Id
                    && mirror.Direction == GitCandy.Remotes.RemoteMirrorDirection.Pull
                    && mirror.IsEnabled),
                cancellationToken))
        {
            return false;
        }

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
            join repository in repositories
                on role.RepositoryId equals repository.Id
            where role.UserId == userId
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
            join repository in repositories
                on teamRole.RepositoryId equals repository.Id
            join userTeamRole in _dbContext.UserTeamRoles.AsNoTracking()
                on teamRole.TeamId equals userTeamRole.TeamId
            where userTeamRole.UserId == userId
                && teamRole.AllowRead
                && (!requiresWrite || teamRole.AllowWrite)
            select teamRole)
            .AnyAsync(cancellationToken);
    }

    private IQueryable<GitCandyRepository> SelectRepositories(string? repositoryName, long? repositoryId)
    {
        if (repositoryId is not null)
        {
            return _dbContext.Repositories.AsNoTracking()
                .Where(repository => repository.Id == repositoryId.Value);
        }

        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(repositoryName!);
        return from repository in _dbContext.Repositories.AsNoTracking()
            where _dbContext.LegacyRepositoryRoutes.Any(route =>
                    route.RepositoryId == repository.Id && route.NormalizedProject == normalizedName)
                || (!_dbContext.LegacyRepositoryRoutes.Any(route => route.NormalizedProject == normalizedName)
                    && repository.NormalizedName == normalizedName)
            select repository;
    }
}
