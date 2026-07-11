using GitCandy.Data;
using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>
/// 基于 EF Core 领域表的团队管理服务。
/// </summary>
internal sealed class TeamService(
    GitCandyDbContext dbContext,
    INamespaceProvisioningService namespaceProvisioningService) : ITeamService
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly INamespaceProvisioningService _namespaceProvisioningService = namespaceProvisioningService;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TeamSummary>> GetTeamsAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var teams = _dbContext.Teams.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var queryText = query.Trim();
            var normalizedQuery = queryText.ToUpperInvariant();
            var queryPattern = $"%{queryText}%";
            teams = teams.Where(team =>
                team.NormalizedName.Contains(normalizedQuery)
                || EF.Functions.Like(team.Description, queryPattern));
        }

        return await teams
            .OrderBy(team => team.NormalizedName)
            .Select(team => new TeamSummary(
                team.Name,
                team.DisplayName,
                team.Description,
                _dbContext.UserTeamRoles.Count(role => role.TeamId == team.Id)))
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TeamDetails?> GetTeamAsync(
        string teamName,
        string? viewerUserId,
        bool viewerIsAdministrator,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeTeamName(teamName);
        var team = await _dbContext.Teams
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.NormalizedName == normalizedName, cancellationToken);
        if (team is null)
        {
            return null;
        }

        var members = await (
                from role in _dbContext.UserTeamRoles.AsNoTracking()
                join user in _dbContext.Users.AsNoTracking() on role.UserId equals user.Id
                where role.TeamId == team.Id
                orderby user.NormalizedUserName
                select new TeamMemberSummary(
                    user.UserName ?? string.Empty,
                    user.DisplayName ?? user.UserName ?? string.Empty,
                    role.IsAdministrator))
            .ToArrayAsync(cancellationToken);
        var repositories = await (
                from role in _dbContext.TeamRepositoryRoles.AsNoTracking()
                join repository in _dbContext.Repositories.AsNoTracking()
                    on role.RepositoryId equals repository.Id
                where role.TeamId == team.Id
                    && ((viewerIsAdministrator && viewerUserId != null)
                        || (!repository.IsPrivate && repository.AllowAnonymousRead)
                        || (viewerUserId != null
                            && (_dbContext.UserRepositoryRoles.Any(userRole =>
                                    userRole.RepositoryId == repository.Id
                                    && userRole.UserId == viewerUserId
                                    && userRole.AllowRead)
                                || _dbContext.TeamRepositoryRoles.Any(viewerTeamRole =>
                                    viewerTeamRole.RepositoryId == repository.Id
                                    && viewerTeamRole.AllowRead
                                    && _dbContext.UserTeamRoles.Any(userTeamRole =>
                                        userTeamRole.TeamId == viewerTeamRole.TeamId
                                        && userTeamRole.UserId == viewerUserId)))))
                orderby repository.NormalizedName
                select repository.Name)
            .ToArrayAsync(cancellationToken);

        return new TeamDetails(team.Name, team.DisplayName, team.Description, members, repositories);
    }

    /// <inheritdoc />
    public async Task<bool> CreateTeamAsync(
        string name,
        string displayName,
        string description,
        string creatorUserId,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeTeamName(name);
        if (await _dbContext.Teams.AnyAsync(team => team.NormalizedName == normalizedName, cancellationToken))
        {
            return false;
        }

        var team = new GitCandyTeam
        {
            Name = name.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? name.Trim() : displayName.Trim(),
            Description = description.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        team.UserRoles.Add(new GitCandyUserTeamRole
        {
            UserId = creatorUserId,
            IsAdministrator = true
        });
        _dbContext.Teams.Add(team);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await _namespaceProvisioningService.EnsureTeamNamespaceAsync(team.Id, cancellationToken) is not null;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateTeamAsync(
        string name,
        string displayName,
        string description,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeTeamName(name);
        var team = await _dbContext.Teams.SingleOrDefaultAsync(
            item => item.NormalizedName == normalizedName,
            cancellationToken);
        if (team is null)
        {
            return false;
        }

        team.DisplayName = string.IsNullOrWhiteSpace(displayName) ? team.Name : displayName.Trim();
        team.Description = description.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteTeamAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeTeamName(name);
        var team = await _dbContext.Teams.SingleOrDefaultAsync(
            item => item.NormalizedName == normalizedName,
            cancellationToken);
        if (team is null)
        {
            return false;
        }

        var namespaceItem = await _dbContext.Namespaces.SingleOrDefaultAsync(
            item => item.TeamId == team.Id,
            cancellationToken);
        if (namespaceItem is not null)
        {
            namespaceItem.OwnerType = NamespaceOwnerType.System;
            namespaceItem.TeamId = null;
        }

        _dbContext.Teams.Remove(team);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetMemberAsync(
        string teamName,
        string userName,
        TeamMemberAction action,
        CancellationToken cancellationToken = default)
    {
        var normalizedTeamName = GitCandyNameNormalizer.NormalizeTeamName(teamName);
        var teamId = await _dbContext.Teams
            .Where(team => team.NormalizedName == normalizedTeamName)
            .Select(team => (long?)team.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var normalizedUserName = userName.Trim().ToUpperInvariant();
        var userId = await _dbContext.Users
            .Where(user => user.NormalizedUserName == normalizedUserName)
            .Select(user => user.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (teamId is null || userId is null)
        {
            return false;
        }

        var role = await _dbContext.UserTeamRoles.SingleOrDefaultAsync(
            item => item.TeamId == teamId.Value && item.UserId == userId,
            cancellationToken);
        switch (action)
        {
            case TeamMemberAction.Add when role is null:
                _dbContext.UserTeamRoles.Add(new GitCandyUserTeamRole
                {
                    TeamId = teamId.Value,
                    UserId = userId
                });
                break;
            case TeamMemberAction.Remove when role is not null:
                _dbContext.UserTeamRoles.Remove(role);
                break;
            case TeamMemberAction.MakeAdministrator when role is not null:
                role.IsAdministrator = true;
                break;
            case TeamMemberAction.MakeMember when role is not null:
                role.IsAdministrator = false;
                break;
            default:
                return false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
