namespace GitCandy.Data.Domain;

/// <summary>在稳定 namespace 内直接指向仓库 ID 的历史名称。</summary>
public sealed class GitCandyRepositoryAlias
{
    public long Id { get; set; }

    public long NamespaceId { get; set; }

    public long RepositoryId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string NormalizedSlug { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? ReleasedAtUtc { get; set; }

    public GitCandyNamespace? Namespace { get; set; }

    public GitCandyRepository? Repository { get; set; }
}
