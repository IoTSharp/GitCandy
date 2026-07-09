namespace GitCandy.Data.Domain;

/// <summary>
/// 团队到仓库的权限角色。
/// </summary>
public sealed class GitCandyTeamRepositoryRole
{
    /// <summary>
    /// 团队主键。
    /// </summary>
    public long TeamId { get; set; }

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
    /// 团队。
    /// </summary>
    public GitCandyTeam Team { get; set; } = null!;

    /// <summary>
    /// 仓库。
    /// </summary>
    public GitCandyRepository Repository { get; set; } = null!;
}
