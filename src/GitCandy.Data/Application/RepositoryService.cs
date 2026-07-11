using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Permissions;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>
/// 基于 EF Core 领域表的仓库应用服务。
/// </summary>
internal sealed class RepositoryService(
    GitCandyDbContext dbContext,
    IGitCandyRepositoryPermissionQuery permissionQuery)
    : IRepositoryService
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly IGitCandyRepositoryPermissionQuery _permissionQuery = permissionQuery;

    /// <inheritdoc />
    public async Task<RepositorySummary?> FindRepositoryAsync(
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(repositoryName);

        var mapped = await SelectSummary(
            from repository in _dbContext.Repositories.AsNoTracking()
            join route in _dbContext.LegacyRepositoryRoutes.AsNoTracking()
                on repository.Id equals route.RepositoryId
            where route.NormalizedProject == normalizedName
            select repository).SingleOrDefaultAsync(cancellationToken);
        if (mapped is not null)
        {
            return mapped;
        }

        var fallback = await SelectSummary(_dbContext.Repositories.AsNoTracking()
                .Where(repository => repository.NormalizedName == normalizedName))
            .Take(2)
            .ToArrayAsync(cancellationToken);
        return fallback.Length == 1 ? fallback[0] : null;
    }

    /// <inheritdoc />
    public Task<RepositorySummary?> FindRepositoryAsync(
        long repositoryId,
        CancellationToken cancellationToken = default)
    {
        return SelectSummary(_dbContext.Repositories.AsNoTracking()
                .Where(repository => repository.Id == repositoryId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CanReadRepositoryAsync(
        long repositoryId,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        return _permissionQuery.CanReadRepositoryAsync(repositoryId, userId, isAdministrator, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepositorySummary>> GetVisibleRepositoriesAsync(
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        var repositories = _dbContext.Repositories.AsNoTracking();

        if (isAdministrator && !string.IsNullOrWhiteSpace(userId))
        {
            return await SelectSummary(repositories.OrderBy(repository => repository.NormalizedName))
                .ToArrayAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await SelectSummary(repositories
                    .Where(repository => !repository.IsPrivate && repository.AllowAnonymousRead)
                    .OrderBy(repository => repository.NormalizedName))
                .ToArrayAsync(cancellationToken);
        }

        return await SelectSummary(repositories
                .Where(repository =>
                    (!repository.IsPrivate && repository.AllowAnonymousRead)
                    || _dbContext.UserRepositoryRoles.Any(role =>
                        role.RepositoryId == repository.Id
                        && role.UserId == userId
                        && role.AllowRead)
                    || _dbContext.TeamRepositoryRoles.Any(teamRole =>
                        teamRole.RepositoryId == repository.Id
                        && teamRole.AllowRead
                        && _dbContext.UserTeamRoles.Any(userTeamRole =>
                            userTeamRole.TeamId == teamRole.TeamId
                            && userTeamRole.UserId == userId)))
                .OrderBy(repository => repository.NormalizedName))
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CanReadRepositoryAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        return _permissionQuery.CanReadRepositoryAsync(
            repositoryName,
            userId,
            isAdministrator,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CanWriteRepositoryAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        return _permissionQuery.CanWriteRepositoryAsync(
            repositoryName,
            userId,
            isAdministrator,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CanWriteRepositoryAsync(
        long repositoryId,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        return _permissionQuery.CanWriteRepositoryAsync(repositoryId, userId, isAdministrator, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsRepositoryOwnerAsync(
        string repositoryName,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        return _permissionQuery.IsRepositoryOwnerAsync(
            repositoryName,
            userId,
            isAdministrator,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsRepositoryOwnerAsync(
        long repositoryId,
        string? userId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        return _permissionQuery.IsRepositoryOwnerAsync(repositoryId, userId, isAdministrator, cancellationToken);
    }

    private static IQueryable<RepositorySummary> SelectSummary(IQueryable<GitCandyRepository> repositories)
    {
        return repositories.Select(repository => new RepositorySummary(
            repository.Id,
            repository.NamespaceId,
            repository.Namespace!.Slug,
            repository.Name,
            repository.NormalizedName,
            repository.StorageName,
            repository.Description,
            repository.IsPrivate,
            repository.AllowAnonymousRead,
            repository.AllowAnonymousWrite,
            repository.CreatedAtUtc));
    }
}
