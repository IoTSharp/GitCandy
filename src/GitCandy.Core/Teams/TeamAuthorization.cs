namespace GitCandy.Teams;

/// <summary>
/// 团队治理授权服务。系统管理员可以执行团队治理操作，但不能绕过最后一位 TeamOwner 不变量。
/// </summary>
public interface ITeamAuthorizationService
{
    /// <summary>读取用户在团队中的治理角色。</summary>
    Task<TeamRole?> GetRoleAsync(
        string teamName,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>判断用户是否具有指定团队权限。</summary>
    Task<bool> IsAllowedAsync(
        string teamName,
        string? userId,
        bool isSystemAdministrator,
        TeamPermission permission,
        CancellationToken cancellationToken = default);
}
