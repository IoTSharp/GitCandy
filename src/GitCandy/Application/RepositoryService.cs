using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Permissions;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>
/// 基于 EF Core 领域表的仓库应用服务。
/// </summary>
public sealed class RepositoryService(
    GitCandyDbContext dbContext,
    IGitCandyRepositoryPermissionQuery permissionQuery)
    : IRepositoryService
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly IGitCandyRepositoryPermissionQuery _permissionQuery = permissionQuery;

    /// <inheritdoc />
    public Task<RepositorySummary?> FindRepositoryAsync(
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(repositoryName);

        return SelectSummary(_dbContext.Repositories
                .AsNoTracking()
                .Where(repository => repository.NormalizedName == normalizedName))
            .SingleOrDefaultAsync(cancellationToken);
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

    private static IQueryable<RepositorySummary> SelectSummary(IQueryable<GitCandyRepository> repositories)
    {
        return repositories.Select(repository => new RepositorySummary(
            repository.Name,
            repository.NormalizedName,
            repository.Description,
            repository.IsPrivate,
            repository.AllowAnonymousRead,
            repository.AllowAnonymousWrite,
            repository.CreatedAtUtc));
    }
}
