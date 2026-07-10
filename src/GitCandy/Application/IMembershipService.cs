using GitCandy.Data.Identity;

namespace GitCandy.Application;

/// <summary>
/// GitCandy 用户、登录标识和管理员角色的应用服务入口。
/// </summary>
public interface IMembershipService
{
    /// <summary>
    /// 按用户名或邮箱查找 Identity 用户。
    /// </summary>
    /// <param name="userNameOrEmail">用户名或邮箱。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配的用户；不存在时返回 <see langword="null" />。</returns>
    Task<GitCandyUser?> FindUserAsync(
        string userNameOrEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断用户是否属于 GitCandy 系统管理员角色。
    /// </summary>
    /// <param name="userId">Identity 用户主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>若用户属于管理员角色则为 <see langword="true" />。</returns>
    Task<bool> IsAdministratorAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断用户是否为指定团队的管理员。
    /// </summary>
    /// <param name="teamName">团队名称。</param>
    /// <param name="userId">Identity 用户主键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>若用户为团队管理员则为 <see langword="true" />。</returns>
    Task<bool> IsTeamAdministratorAsync(
        string teamName,
        string userId,
        CancellationToken cancellationToken = default);
}
