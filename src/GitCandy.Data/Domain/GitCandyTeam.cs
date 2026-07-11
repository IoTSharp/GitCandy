namespace GitCandy.Data.Domain;

/// <summary>
/// GitCandy 团队领域实体。
/// </summary>
public sealed class GitCandyTeam
{
    /// <summary>
    /// 团队主键。
    /// </summary>
    public long Id { get; set; }

    /// <summary>团队显示名称；修改它不改变 URL。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 团队名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 规范化团队名称，用于大小写不敏感查找和唯一约束。
    /// </summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>
    /// 团队描述。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 团队创建时间。
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>团队拥有的稳定 namespace。</summary>
    public GitCandyNamespace? Namespace { get; set; }

    /// <summary>
    /// 团队成员角色。
    /// </summary>
    public ICollection<GitCandyUserTeamRole> UserRoles { get; } = [];

    /// <summary>
    /// 团队仓库角色。
    /// </summary>
    public ICollection<GitCandyTeamRepositoryRole> RepositoryRoles { get; } = [];
}
