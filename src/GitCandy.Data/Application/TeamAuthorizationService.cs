using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Teams;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class TeamAuthorizationService(GitCandyDbContext dbContext)
    : ITeamAuthorizationService
{
    private readonly GitCandyDbContext _dbContext = dbContext;

    public Task<TeamRole?> GetRoleAsync(
        string teamName,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var normalizedName = GitCandyNameNormalizer.NormalizeTeamName(teamName);
        return (
            from role in _dbContext.UserTeamRoles.AsNoTracking()
            join team in _dbContext.Teams.AsNoTracking() on role.TeamId equals team.Id
            where team.NormalizedName == normalizedName && role.UserId == userId
            select (TeamRole?)role.Role)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> IsAllowedAsync(
        string teamName,
        string? userId,
        bool isSystemAdministrator,
        TeamPermission permission,
        CancellationToken cancellationToken = default)
    {
        if (isSystemAdministrator)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var role = await GetRoleAsync(teamName, userId, cancellationToken);
        return role is not null && TeamRolePermissions.Allows(role.Value, permission);
    }
}
