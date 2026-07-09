using GitCandy.Data.Identity;

namespace GitCandy.Data.Domain;

/// <summary>
/// 用户到团队的成员角色。
/// </summary>
public sealed class GitCandyUserTeamRole
{
    /// <summary>
    /// Identity 用户主键。
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// 团队主键。
    /// </summary>
    public long TeamId { get; set; }

    /// <summary>
    /// 是否为团队管理员。
    /// </summary>
    public bool IsAdministrator { get; set; }

    /// <summary>
    /// Identity 用户。
    /// </summary>
    public GitCandyUser User { get; set; } = null!;

    /// <summary>
    /// 团队。
    /// </summary>
    public GitCandyTeam Team { get; set; } = null!;
}
