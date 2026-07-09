using GitCandy.Configuration;
using GitCandy.Data.Identity;
using Microsoft.AspNetCore.Identity;

namespace GitCandy.Application;

/// <summary>
/// 基于 ASP.NET Core Identity 的用户应用服务。
/// </summary>
public sealed class MembershipService(UserManager<GitCandyUser> userManager) : IMembershipService
{
    private readonly UserManager<GitCandyUser> _userManager = userManager;

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
}
