using GitCandy.Data;
using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>
/// 基于 EF Core 领域表的仓库元数据管理服务。
/// </summary>
internal sealed class RepositoryManagementService(
    GitCandyDbContext dbContext,
    INamespaceProvisioningService namespaceProvisioningService,
    TimeProvider timeProvider)
    : IRepositoryManagementService
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly INamespaceProvisioningService _namespaceProvisioningService = namespaceProvisioningService;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<RepositoryDetails?> GetRepositoryAsync(
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(repositoryName);
        var repositoryId = await _dbContext.LegacyRepositoryRoutes
            .AsNoTracking()
            .Where(item => item.NormalizedProject == normalizedName)
            .Select(item => (long?)item.RepositoryId)
            .SingleOrDefaultAsync(cancellationToken);
        if (repositoryId is not null)
        {
            return await GetRepositoryAsync(repositoryId.Value, cancellationToken);
        }

        var fallbackIds = await _dbContext.Repositories.AsNoTracking()
            .Where(item => item.NormalizedName == normalizedName)
            .Select(item => item.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        return fallbackIds.Length == 1
            ? await GetRepositoryAsync(fallbackIds[0], cancellationToken)
            : null;
    }

    /// <inheritdoc />
    public async Task<RepositoryDetails?> GetRepositoryAsync(
        long repositoryId,
        CancellationToken cancellationToken = default)
    {
        var repository = await _dbContext.Repositories
            .AsNoTracking()
            .Include(item => item.Namespace)
            .SingleOrDefaultAsync(item => item.Id == repositoryId, cancellationToken);
        if (repository is null)
        {
            return null;
        }

        var users = await (
                from role in _dbContext.UserRepositoryRoles.AsNoTracking()
                join user in _dbContext.Users.AsNoTracking() on role.UserId equals user.Id
                where role.RepositoryId == repository.Id
                orderby user.NormalizedUserName
                select new RepositoryUserRoleSummary(
                    user.UserName ?? string.Empty,
                    role.AllowRead,
                    role.AllowWrite,
                    role.IsOwner))
            .ToArrayAsync(cancellationToken);
        var teams = await (
                from role in _dbContext.TeamRepositoryRoles.AsNoTracking()
                join team in _dbContext.Teams.AsNoTracking() on role.TeamId equals team.Id
                where role.RepositoryId == repository.Id
                orderby team.NormalizedName
                select new RepositoryTeamRoleSummary(team.Name, role.AllowRead, role.AllowWrite))
            .ToArrayAsync(cancellationToken);

        return new RepositoryDetails(
            repository.Id,
            repository.NamespaceId,
            repository.Namespace!.Slug,
            repository.Name,
            repository.StorageName,
            repository.Description,
            repository.IsPrivate,
            repository.AllowAnonymousRead,
            repository.AllowAnonymousWrite,
            repository.CreatedAtUtc,
            repository.ForkedFromRepository,
            repository.ForkNetworkRoot,
            users,
            teams);
    }

    /// <inheritdoc />
    public async Task<bool> CreateRepositoryAsync(
        RepositoryEdit command,
        string creatorUserId,
        CancellationToken cancellationToken = default)
    {
        var namespaceId = await ResolveCreationNamespaceAsync(command.NamespaceSlug, creatorUserId, cancellationToken);
        if (namespaceId is null)
        {
            return false;
        }

        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(command.Name);
        if (await _dbContext.RepositoryClaims.AnyAsync(
            claim => claim.NamespaceId == namespaceId.Value && claim.NormalizedSlug == normalizedName,
            cancellationToken))
        {
            return false;
        }

        var repository = CreateEntity(command);
        repository.NamespaceId = namespaceId.Value;
        repository.UserRoles.Add(new GitCandyUserRepositoryRole
        {
            UserId = creatorUserId,
            AllowRead = true,
            AllowWrite = true,
            IsOwner = true
        });
        _dbContext.Repositories.Add(repository);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.RepositoryClaims.Add(new GitCandyRepositoryClaim
        {
            NamespaceId = repository.NamespaceId,
            NormalizedSlug = normalizedName,
            Slug = repository.Name,
            ClaimType = NameClaimType.Current,
            RepositoryId = repository.Id
        });
        if (!await _dbContext.LegacyRepositoryRoutes.AnyAsync(
            route => route.NormalizedProject == normalizedName,
            cancellationToken))
        {
            _dbContext.LegacyRepositoryRoutes.Add(new GitCandyLegacyRepositoryRoute
            {
                Project = repository.Name,
                NormalizedProject = normalizedName,
                RepositoryId = repository.Id,
                CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateRepositoryAsync(
        string repositoryName,
        RepositoryEdit command,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(repositoryName);
        var repository = await _dbContext.Repositories.SingleOrDefaultAsync(
            item => item.NormalizedName == normalizedName,
            cancellationToken);
        if (repository is null)
        {
            return false;
        }

        ApplyVisibility(repository, command);
        repository.Description = command.Description.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteRepositoryAsync(
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(repositoryName);
        var repository = await _dbContext.Repositories.SingleOrDefaultAsync(
            item => item.NormalizedName == normalizedName,
            cancellationToken);
        if (repository is null)
        {
            return false;
        }

        var claims = await _dbContext.RepositoryClaims
            .Include(claim => claim.RepositoryAlias)
            .Where(claim => claim.RepositoryId == repository.Id
                || (claim.RepositoryAlias != null && claim.RepositoryAlias.RepositoryId == repository.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var claim in claims)
        {
            claim.ClaimType = NameClaimType.Reserved;
            claim.RepositoryId = null;
            claim.RepositoryAliasId = null;
            claim.RepositoryAlias = null;
        }

        var aliases = await _dbContext.RepositoryAliases
            .Where(alias => alias.RepositoryId == repository.Id)
            .ToArrayAsync(cancellationToken);
        var legacyRoutes = await _dbContext.LegacyRepositoryRoutes
            .Where(route => route.RepositoryId == repository.Id)
            .ToArrayAsync(cancellationToken);
        _dbContext.RepositoryAliases.RemoveRange(aliases);
        _dbContext.LegacyRepositoryRoutes.RemoveRange(legacyRoutes);
        _dbContext.Repositories.Remove(repository);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetUserRoleAsync(
        string repositoryName,
        string userName,
        RepositoryUserRoleAction action,
        bool value,
        CancellationToken cancellationToken = default)
    {
        var repositoryId = await FindRepositoryIdAsync(repositoryName, cancellationToken);
        var normalizedUserName = userName.Trim().ToUpperInvariant();
        var userId = await _dbContext.Users
            .Where(user => user.NormalizedUserName == normalizedUserName)
            .Select(user => user.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (repositoryId is null || userId is null)
        {
            return false;
        }

        var role = await _dbContext.UserRepositoryRoles.SingleOrDefaultAsync(
            item => item.RepositoryId == repositoryId.Value && item.UserId == userId,
            cancellationToken);
        switch (action)
        {
            case RepositoryUserRoleAction.Add when role is null:
                _dbContext.UserRepositoryRoles.Add(new GitCandyUserRepositoryRole
                {
                    RepositoryId = repositoryId.Value,
                    UserId = userId,
                    AllowRead = true
                });
                break;
            case RepositoryUserRoleAction.Remove when role is not null:
                _dbContext.UserRepositoryRoles.Remove(role);
                break;
            case RepositoryUserRoleAction.SetRead when role is not null:
                role.AllowRead = value;
                if (!value)
                {
                    role.AllowWrite = false;
                }
                break;
            case RepositoryUserRoleAction.SetWrite when role is not null:
                role.AllowWrite = value;
                role.AllowRead |= value;
                break;
            case RepositoryUserRoleAction.SetOwner when role is not null:
                role.IsOwner = value;
                role.AllowRead |= value;
                role.AllowWrite |= value;
                break;
            default:
                return false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetTeamRoleAsync(
        string repositoryName,
        string teamName,
        RepositoryTeamRoleAction action,
        bool value,
        CancellationToken cancellationToken = default)
    {
        var repositoryId = await FindRepositoryIdAsync(repositoryName, cancellationToken);
        var normalizedTeamName = GitCandyNameNormalizer.NormalizeTeamName(teamName);
        var teamId = await _dbContext.Teams
            .Where(team => team.NormalizedName == normalizedTeamName)
            .Select(team => (long?)team.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (repositoryId is null || teamId is null)
        {
            return false;
        }

        var role = await _dbContext.TeamRepositoryRoles.SingleOrDefaultAsync(
            item => item.RepositoryId == repositoryId.Value && item.TeamId == teamId.Value,
            cancellationToken);
        switch (action)
        {
            case RepositoryTeamRoleAction.Add when role is null:
                _dbContext.TeamRepositoryRoles.Add(new GitCandyTeamRepositoryRole
                {
                    RepositoryId = repositoryId.Value,
                    TeamId = teamId.Value,
                    AllowRead = true
                });
                break;
            case RepositoryTeamRoleAction.Remove when role is not null:
                _dbContext.TeamRepositoryRoles.Remove(role);
                break;
            case RepositoryTeamRoleAction.SetRead when role is not null:
                role.AllowRead = value;
                if (!value)
                {
                    role.AllowWrite = false;
                }
                break;
            case RepositoryTeamRoleAction.SetWrite when role is not null:
                role.AllowWrite = value;
                role.AllowRead |= value;
                break;
            default:
                return false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<long?> FindRepositoryIdAsync(
        string repositoryName,
        CancellationToken cancellationToken)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(repositoryName);
        return (
            from repository in _dbContext.Repositories
            where (_dbContext.LegacyRepositoryRoutes.Any(route =>
                    route.RepositoryId == repository.Id && route.NormalizedProject == normalizedName))
                || (!_dbContext.LegacyRepositoryRoutes.Any(route => route.NormalizedProject == normalizedName)
                    && repository.NormalizedName == normalizedName)
            select (long?)repository.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static GitCandyRepository CreateEntity(RepositoryEdit command)
    {
        var repository = new GitCandyRepository
        {
            Name = command.Name.Trim(),
            StorageName = string.IsNullOrWhiteSpace(command.StorageName)
                ? command.Name.Trim()
                : command.StorageName.Trim(),
            Description = command.Description.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            ForkedFromRepository = NormalizeOptionalName(command.ForkedFromRepository),
            ForkNetworkRoot = NormalizeOptionalName(command.ForkNetworkRoot)
        };
        ApplyVisibility(repository, command);
        return repository;
    }

    private async Task<long?> ResolveCreationNamespaceAsync(
        string? namespaceSlug,
        string creatorUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(namespaceSlug))
        {
            return await _namespaceProvisioningService.EnsureUserNamespaceAsync(
                creatorUserId,
                cancellationToken);
        }

        var normalizedSlug = NamespaceSlugRules.Normalize(namespaceSlug);
        return await _dbContext.Namespaces
            .Where(item => item.NormalizedSlug == normalizedSlug
                && (item.UserId == creatorUserId
                    || (item.TeamId != null && _dbContext.UserTeamRoles.Any(role =>
                        role.TeamId == item.TeamId && role.UserId == creatorUserId && role.IsAdministrator))))
            .Select(item => (long?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static void ApplyVisibility(GitCandyRepository repository, RepositoryEdit command)
    {
        repository.IsPrivate = command.IsPrivate;
        repository.AllowAnonymousRead = !command.IsPrivate && command.AllowAnonymousRead;
        repository.AllowAnonymousWrite = repository.AllowAnonymousRead && command.AllowAnonymousWrite;
    }

    private static string? NormalizeOptionalName(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
