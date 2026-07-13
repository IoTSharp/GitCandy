using GitCandy.Data.Identity;
using GitCandy.Teams;

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
    /// 团队治理角色。
    /// </summary>
    public TeamRole Role { get; set; } = TeamRole.Member;

    /// <summary>
    /// Identity 用户；未显式加载导航属性时为空。
    /// </summary>
    public GitCandyUser? User { get; set; }

    /// <summary>
    /// 团队；未显式加载导航属性时为空。
    /// </summary>
    public GitCandyTeam? Team { get; set; }
}
