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

    /// <summary>稳定 namespace 主键。</summary>
    public long NamespaceId { get; set; } = GitCandyNamespace.LegacyNamespaceId;

    /// <summary>与可变 URL slug 分离的物理仓库存储键。</summary>
    public string StorageName { get; set; } = string.Empty;

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
    /// 直接 fork 来源仓库名称；非 fork 仓库为空。
    /// </summary>
    public string? ForkedFromRepository { get; set; }

    /// <summary>
    /// fork network 根仓库名称；非 fork 仓库为空。
    /// </summary>
    public string? ForkNetworkRoot { get; set; }

    /// <summary>仓库所属稳定 namespace。</summary>
    public GitCandyNamespace? Namespace { get; set; }

    /// <summary>仓库历史名称。</summary>
    public ICollection<GitCandyRepositoryAlias> Aliases { get; } = [];

    /// <summary>
    /// 用户仓库角色。
    /// </summary>
    public ICollection<GitCandyUserRepositoryRole> UserRoles { get; } = [];

    /// <summary>
    /// 团队仓库角色。
    /// </summary>
    public ICollection<GitCandyTeamRepositoryRole> TeamRoles { get; } = [];

    /// <summary>仓库级共享 work item 编号序列。</summary>
    public GitCandyWorkItemSequence? WorkItemSequence { get; set; }

    /// <summary>仓库 Issues。</summary>
    public ICollection<GitCandyIssue> Issues { get; } = [];

    /// <summary>仓库 Pull Requests。</summary>
    public ICollection<GitCandyPullRequest> PullRequests { get; } = [];
}
