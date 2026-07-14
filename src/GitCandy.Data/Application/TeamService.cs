using System.Data;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Teams;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>
/// 基于 EF Core 领域表的团队管理服务。
/// </summary>
internal sealed class TeamService(
    GitCandyDbContext dbContext,
    INamespaceProvisioningService namespaceProvisioningService,
    ITeamAuthorizationService teamAuthorizationService,
    TimeProvider timeProvider) : ITeamService
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly INamespaceProvisioningService _namespaceProvisioningService = namespaceProvisioningService;
    private readonly ITeamAuthorizationService _teamAuthorizationService = teamAuthorizationService;
    private readonly TimeProvider _timeProvider = timeProvider;

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
                    role.Role))
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
            Role = TeamRole.TeamOwner
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
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (!await _teamAuthorizationService.IsAllowedAsync(
                name,
                actorUserId,
                actorIsSystemAdministrator,
                TeamPermission.RenameTeam,
                cancellationToken))
        {
            return false;
        }

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
        await AddAuditAsync(
            team,
            actorUserId,
            "team.update",
            "succeeded",
            team.Name,
            "Team profile updated.",
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteTeamAsync(
        string name,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (!await _teamAuthorizationService.IsAllowedAsync(
                name,
                actorUserId,
                actorIsSystemAdministrator,
                TeamPermission.DeleteTeam,
                cancellationToken))
        {
            return false;
        }

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

        await AddAuditAsync(
            team,
            actorUserId,
            "team.delete",
            "succeeded",
            team.Name,
            "Team deleted.",
            cancellationToken);
        _dbContext.Teams.Remove(team);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SetMemberAsync(
        string teamName,
        string userName,
        TeamMemberAction action,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        var result = await ApplyMemberChangesAsync(
            teamName,
            [new TeamMemberChange(userName, action)],
            actorUserId,
            actorIsSystemAdministrator,
            cancellationToken);
        return result.Succeeded;
    }

    /// <inheritdoc />
    public async Task<TeamMemberChangeResult> ApplyMemberChangesAsync(
        string teamName,
        IReadOnlyList<TeamMemberChange> changes,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0 || changes.Count > 100)
        {
            return new TeamMemberChangeResult(false, "A batch must contain between 1 and 100 changes.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var normalizedTeamName = GitCandyNameNormalizer.NormalizeTeamName(teamName);
        var team = await _dbContext.Teams
            .SingleOrDefaultAsync(
                item => item.NormalizedName == normalizedTeamName,
                cancellationToken);
        if (team is null)
        {
            return new TeamMemberChangeResult(false, "The team does not exist.");
        }

        var normalizedUserNames = changes
            .Select(change => change.UserName.Trim().ToUpperInvariant())
            .ToArray();
        if (normalizedUserNames.Any(string.IsNullOrWhiteSpace)
            || normalizedUserNames.Distinct(StringComparer.Ordinal).Count() != normalizedUserNames.Length)
        {
            return await RejectMemberBatchAsync(
                team,
                actorUserId,
                "The batch contains an empty or duplicate user name.",
                transaction,
                cancellationToken);
        }

        var users = await _dbContext.Users
            .Where(user => user.NormalizedUserName != null
                && normalizedUserNames.Contains(user.NormalizedUserName))
            .ToDictionaryAsync(user => user.NormalizedUserName!, StringComparer.Ordinal, cancellationToken);
        if (users.Count != normalizedUserNames.Length)
        {
            return await RejectMemberBatchAsync(
                team,
                actorUserId,
                "One or more users do not exist.",
                transaction,
                cancellationToken);
        }

        var roles = await _dbContext.UserTeamRoles
            .Where(role => role.TeamId == team.Id)
            .ToDictionaryAsync(role => role.UserId, StringComparer.Ordinal, cancellationToken);
        TeamRole? actorRole = roles.GetValueOrDefault(actorUserId)?.Role;
        var planned = new List<(GitCandyUserTeamRole? Existing, string UserId, string UserName, TeamRole? Desired)>();
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            var user = users[normalizedUserNames[index]];
            roles.TryGetValue(user.Id, out var existing);
            var desired = GetDesiredRole(change.Action, existing);
            if (desired.Status is null)
            {
                return await RejectMemberBatchAsync(
                    team,
                    actorUserId,
                    "A requested member transition is not valid.",
                    transaction,
                    cancellationToken);
            }

            if (!actorIsSystemAdministrator
                && (actorRole is null
                    || (existing is not null && !TeamRolePermissions.CanManage(actorRole.Value, existing.Role))
                    || (desired.Role is not null && !TeamRolePermissions.CanManage(actorRole.Value, desired.Role.Value))))
            {
                return await RejectMemberBatchAsync(
                    team,
                    actorUserId,
                    "The actor cannot manage one or more requested roles.",
                    transaction,
                    cancellationToken);
            }

            planned.Add((existing, user.Id, user.UserName ?? change.UserName.Trim(), desired.Role));
        }

        var ownerCount = roles.Values.Count(role => role.Role == TeamRole.TeamOwner);
        foreach (var item in planned)
        {
            if (item.Existing?.Role == TeamRole.TeamOwner && item.Desired != TeamRole.TeamOwner)
            {
                ownerCount--;
            }
            else if (item.Existing?.Role != TeamRole.TeamOwner && item.Desired == TeamRole.TeamOwner)
            {
                ownerCount++;
            }
        }

        if (ownerCount < 1)
        {
            return await RejectMemberBatchAsync(
                team,
                actorUserId,
                "A team must retain at least one TeamOwner.",
                transaction,
                cancellationToken);
        }

        var finalOwnerIds = roles.Values
            .Where(role => role.Role == TeamRole.TeamOwner)
            .Select(role => role.UserId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in planned)
        {
            if (item.Existing?.Role == TeamRole.TeamOwner && item.Desired != TeamRole.TeamOwner)
            {
                finalOwnerIds.Remove(item.UserId);
            }
            else if (item.Desired == TeamRole.TeamOwner)
            {
                finalOwnerIds.Add(item.UserId);
            }
        }

        var externallyManagedOwnerIds = await (
                from identity in _dbContext.EnterpriseExternalIdentities.AsNoTracking()
                join connection in _dbContext.EnterpriseConnections.AsNoTracking()
                    on identity.ConnectionId equals connection.Id
                where connection.TeamId == team.Id
                    && identity.IsActive
                    && identity.UserId != null
                    && finalOwnerIds.Contains(identity.UserId)
                select identity.UserId!)
            .ToArrayAsync(cancellationToken);
        if (finalOwnerIds.Except(externallyManagedOwnerIds, StringComparer.Ordinal).Count() == 0)
        {
            return await RejectMemberBatchAsync(
                team,
                actorUserId,
                "A team must retain at least one local break-glass TeamOwner.",
                transaction,
                cancellationToken);
        }

        foreach (var item in planned)
        {
            var previous = item.Existing?.Role;
            if (previous == item.Desired)
            {
                continue;
            }

            if (item.Existing is null)
            {
                _dbContext.UserTeamRoles.Add(new GitCandyUserTeamRole
                {
                    TeamId = team.Id,
                    UserId = item.UserId,
                    Role = item.Desired!.Value
                });
            }
            else if (item.Desired is null)
            {
                _dbContext.UserTeamRoles.Remove(item.Existing);
            }
            else
            {
                item.Existing.Role = item.Desired.Value;
            }

            await AddAuditAsync(
                team,
                actorUserId,
                "team.member.update",
                "succeeded",
                item.UserName,
                $"Role changed from {previous?.ToString() ?? "none"} to {item.Desired?.ToString() ?? "none"}.",
                cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TeamMemberChangeResult.Success;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TeamAuditEventSummary>> GetAuditEventsAsync(
        string teamName,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = GitCandyNameNormalizer.NormalizeTeamName(teamName);
        var teamId = await _dbContext.Teams.AsNoTracking()
            .Where(team => team.NormalizedName == normalizedName)
            .Select(team => (long?)team.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (teamId is null)
        {
            return [];
        }

        return await _dbContext.TeamAuditEvents.AsNoTracking()
            .Where(item => item.TeamId == teamId)
            .OrderByDescending(item => item.OccurredAtUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(item => new TeamAuditEventSummary(
                item.ActorName,
                item.Action,
                item.Outcome,
                item.Subject,
                item.Detail,
                new DateTimeOffset(DateTime.SpecifyKind(item.OccurredAtUtc, DateTimeKind.Utc))))
            .ToArrayAsync(cancellationToken);
    }

    private static (bool? Status, TeamRole? Role) GetDesiredRole(
        TeamMemberAction action,
        GitCandyUserTeamRole? existing) => action switch
    {
        TeamMemberAction.Add when existing is null => (true, TeamRole.Member),
        TeamMemberAction.Remove when existing is not null => (true, null),
        TeamMemberAction.MakeTeamOwner when existing is not null => (true, TeamRole.TeamOwner),
        TeamMemberAction.MakeLeader when existing is not null => (true, TeamRole.Leader),
        TeamMemberAction.MakeDeputyLeader when existing is not null => (true, TeamRole.DeputyLeader),
        TeamMemberAction.MakeMember when existing is not null => (true, TeamRole.Member),
        _ => (null, null)
    };

    private async Task<TeamMemberChangeResult> RejectMemberBatchAsync(
        GitCandyTeam team,
        string actorUserId,
        string error,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await AddAuditAsync(
            team,
            actorUserId,
            "team.member.batch",
            "rejected",
            team.Name,
            error,
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TeamMemberChangeResult(false, error);
    }

    private async Task AddAuditAsync(
        GitCandyTeam team,
        string actorUserId,
        string action,
        string outcome,
        string subject,
        string detail,
        CancellationToken cancellationToken)
    {
        var actorName = await _dbContext.Users.AsNoTracking()
            .Where(user => user.Id == actorUserId)
            .Select(user => user.UserName)
            .SingleOrDefaultAsync(cancellationToken);
        _dbContext.TeamAuditEvents.Add(new GitCandyTeamAuditEvent
        {
            TeamId = team.Id,
            TeamName = team.Name,
            ActorUserId = actorUserId,
            ActorName = actorName ?? "system-administrator",
            Action = action,
            Outcome = outcome,
            Subject = subject,
            Detail = detail,
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        });
    }
}
