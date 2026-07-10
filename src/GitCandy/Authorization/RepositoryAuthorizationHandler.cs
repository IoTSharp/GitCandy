using System.Security.Claims;
using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Configuration;
using Microsoft.AspNetCore.Authorization;

namespace GitCandy.Authorization;

/// <summary>
/// 将仓库资源授权要求委托给统一仓库权限服务。
/// </summary>
public sealed class RepositoryAuthorizationHandler(
    IRepositoryService repositoryService,
    ICurrentUser currentUser)
    : AuthorizationHandler<RepositoryAuthorizationRequirement, RepositoryAuthorizationResource>
{
    private readonly IRepositoryService _repositoryService = repositoryService;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RepositoryAuthorizationRequirement requirement,
        RepositoryAuthorizationResource resource)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdministrator = context.User.IsInRole(RoleNames.Administrator);

        var authorized = requirement.Permission switch
        {
            RepositoryPermission.Read => await _repositoryService.CanReadRepositoryAsync(
                resource.RepositoryName,
                userId,
                isAdministrator,
                _currentUser.RequestAborted),
            RepositoryPermission.Write => await _repositoryService.CanWriteRepositoryAsync(
                resource.RepositoryName,
                userId,
                isAdministrator,
                _currentUser.RequestAborted),
            RepositoryPermission.Owner => await _repositoryService.IsRepositoryOwnerAsync(
                resource.RepositoryName,
                userId,
                isAdministrator,
                _currentUser.RequestAborted),
            _ => false
        };

        if (authorized)
        {
            context.Succeed(requirement);
        }
    }
}
