using GitCandy.Data.Identity;

namespace GitCandy.Data.Domain;

/// <summary>用户、团队或迁移兼容入口拥有的稳定命名空间。</summary>
public sealed class GitCandyNamespace
{
    /// <summary>迁移期无明确 owner 仓库使用的保留 namespace ID。</summary>
    public const long LegacyNamespaceId = 1;

    public long Id { get; set; }

    public NamespaceOwnerType OwnerType { get; set; }

    public string? UserId { get; set; }

    public long? TeamId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string NormalizedSlug { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public int Version { get; set; }

    public GitCandyUser? User { get; set; }

    public GitCandyTeam? Team { get; set; }

    public ICollection<GitCandyRepository> Repositories { get; } = [];

    public ICollection<GitCandyNamespaceAlias> Aliases { get; } = [];
}

/// <summary>稳定 namespace owner 类型。</summary>
public enum NamespaceOwnerType
{
    System = 0,
    User = 1,
    Team = 2
}
