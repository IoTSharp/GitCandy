namespace GitCandy.Data.Domain;

/// <summary>直接指向稳定 namespace ID 的历史名称。</summary>
public sealed class GitCandyNamespaceAlias
{
    public long Id { get; set; }

    public long NamespaceId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string NormalizedSlug { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? ReleasedAtUtc { get; set; }

    public GitCandyNamespace? Namespace { get; set; }
}
