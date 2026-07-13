using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Teams;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>
/// 基于 ASP.NET Core Identity 的用户应用服务。
/// </summary>
internal sealed class MembershipService(
    UserManager<GitCandyUser> userManager,
    GitCandyDbContext dbContext) : IMembershipService
{
    private readonly UserManager<GitCandyUser> _userManager = userManager;
    private readonly GitCandyDbContext _dbContext = dbContext;

    /// <inheritdoc />
    public async Task<GitCandyUser?> FindUserAsync(
        string userNameOrEmail,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userNameOrEmail);

        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByNameAsync(userNameOrEmail);
        if (user is not null)
        {
            return user;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await _userManager.FindByEmailAsync(userNameOrEmail);
    }

    /// <inheritdoc />
    public async Task<bool> IsAdministratorAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        cancellationToken.ThrowIfCancellationRequested();

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return await _userManager.IsInRoleAsync(user, RoleNames.Administrator);
    }

    /// <inheritdoc />
    public Task<bool> IsTeamAdministratorAsync(
        string teamName,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var normalizedName = GitCandyNameNormalizer.NormalizeTeamName(teamName);

        return (
            from userTeamRole in _dbContext.UserTeamRoles.AsNoTracking()
            join team in _dbContext.Teams.AsNoTracking()
                on userTeamRole.TeamId equals team.Id
            where team.NormalizedName == normalizedName
                && userTeamRole.UserId == userId
                && userTeamRole.Role == TeamRole.TeamOwner
            select userTeamRole)
            .AnyAsync(cancellationToken);
    }
}
