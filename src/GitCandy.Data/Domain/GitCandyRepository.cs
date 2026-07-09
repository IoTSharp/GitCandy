namespace GitCandy.Data.Domain;

/// <summary>
/// GitCandy 仓库领域实体。
/// </summary>
public sealed class GitCandyRepository
{
    /// <summary>
    /// 仓库主键。
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 仓库名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 规范化仓库名称，用于大小写不敏感查找和唯一约束。
    /// </summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>
    /// 仓库描述。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 仓库创建时间。
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// 仓库是否为私有仓库。
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>
    /// 是否允许匿名读取。
    /// </summary>
    public bool AllowAnonymousRead { get; set; }

    /// <summary>
    /// 是否允许匿名写入。只有同时允许匿名读取时才生效。
    /// </summary>
    public bool AllowAnonymousWrite { get; set; }

    /// <summary>
    /// 用户仓库角色。
    /// </summary>
    public ICollection<GitCandyUserRepositoryRole> UserRoles { get; } = [];

    /// <summary>
    /// 团队仓库角色。
    /// </summary>
    public ICollection<GitCandyTeamRepositoryRole> TeamRoles { get; } = [];
}
