using System.Security.Claims;
using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Configuration;
using Microsoft.AspNetCore.Authorization;

namespace GitCandy.Authorization;

/// <summary>
/// 校验 TeamOwner 或系统管理员权限。
/// </summary>
public sealed class TeamAdministratorAuthorizationHandler(
    IMembershipService membershipService,
    ICurrentUser currentUser)
    : AuthorizationHandler<TeamAdministratorRequirement, TeamAuthorizationResource>
{
    private readonly IMembershipService _membershipService = membershipService;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TeamAdministratorRequirement requirement,
        TeamAuthorizationResource resource)
    {
        if (context.User.IsInRole(RoleNames.Administrator))
        {
            context.Succeed(requirement);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId)
            && await _membershipService.IsTeamAdministratorAsync(
                resource.TeamName,
                userId,
                _currentUser.RequestAborted))
        {
            context.Succeed(requirement);
        }
    }
}
