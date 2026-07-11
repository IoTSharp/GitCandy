using GitCandy.Data;
using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>
/// 基于 EF Core 领域表的仓库元数据管理服务。
/// </summary>
internal sealed class RepositoryManagementService(GitCandyDbContext dbContext)
    : IRepositoryManagementService
{
    private readonly GitCandyDbContext _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<RepositoryDetails?> GetRepositoryAsync(
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(repositoryName);
        var repository = await _dbContext.Repositories
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.NormalizedName == normalizedName, cancellationToken);
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
            repository.Name,
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
        var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(command.Name);
        if (await _dbContext.Repositories.AnyAsync(
            repository => repository.NormalizedName == normalizedName,
            cancellationToken))
        {
            return false;
        }

        var repository = CreateEntity(command);
        repository.UserRoles.Add(new GitCandyUserRepositoryRole
        {
            UserId = creatorUserId,
            AllowRead = true,
            AllowWrite = true,
            IsOwner = true
        });
        _dbContext.Repositories.Add(repository);
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
        return _dbContext.Repositories
            .Where(repository => repository.NormalizedName == normalizedName)
            .Select(repository => (long?)repository.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static GitCandyRepository CreateEntity(RepositoryEdit command)
    {
        var repository = new GitCandyRepository
        {
            Name = command.Name.Trim(),
            Description = command.Description.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            ForkedFromRepository = NormalizeOptionalName(command.ForkedFromRepository),
            ForkNetworkRoot = NormalizeOptionalName(command.ForkNetworkRoot)
        };
        ApplyVisibility(repository, command);
        return repository;
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
