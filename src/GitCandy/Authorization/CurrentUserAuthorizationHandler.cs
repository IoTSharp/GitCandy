using GitCandy.Configuration;
using Microsoft.AspNetCore.Authorization;

namespace GitCandy.Authorization;

/// <summary>
/// 校验目标账户是否为当前用户自身，或当前用户是否为系统管理员。
/// </summary>
public sealed class CurrentUserAuthorizationHandler
    : AuthorizationHandler<CurrentUserRequirement, CurrentUserAuthorizationResource>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CurrentUserRequirement requirement,
        CurrentUserAuthorizationResource resource)
    {
        if (context.User.IsInRole(RoleNames.Administrator)
            || (context.User.Identity?.IsAuthenticated == true
                && (string.IsNullOrWhiteSpace(resource.UserName)
                    || string.Equals(
                        context.User.Identity.Name,
                        resource.UserName,
                        StringComparison.OrdinalIgnoreCase))))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
