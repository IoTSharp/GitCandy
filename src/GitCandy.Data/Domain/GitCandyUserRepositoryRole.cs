using GitCandy.Data.Identity;

namespace GitCandy.Data.Domain;

/// <summary>
/// 用户到仓库的权限角色。
/// </summary>
public sealed class GitCandyUserRepositoryRole
{
    /// <summary>
    /// Identity 用户主键。
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// 仓库主键。
    /// </summary>
    public long RepositoryId { get; set; }

    /// <summary>
    /// 是否允许读取仓库。
    /// </summary>
    public bool AllowRead { get; set; }

    /// <summary>
    /// 是否允许写入仓库。写入权限需要同时具备读取权限。
    /// </summary>
    public bool AllowWrite { get; set; }

    /// <summary>
    /// 是否为仓库 owner。
    /// </summary>
    public bool IsOwner { get; set; }

    /// <summary>
    /// Identity 用户；未显式加载导航属性时为空。
    /// </summary>
    public GitCandyUser? User { get; set; }

    /// <summary>
    /// 仓库；未显式加载导航属性时为空。
    /// </summary>
    public GitCandyRepository? Repository { get; set; }
}
