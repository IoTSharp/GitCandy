namespace GitCandy.Data.Domain;

/// <summary>当前 namespace、alias 和系统保留路由共享的全局名称占用。</summary>
public sealed class GitCandyNamespaceClaim
{
    public string NormalizedSlug { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public NameClaimType ClaimType { get; set; }

    public long? NamespaceId { get; set; }

    public long? NamespaceAliasId { get; set; }

    public GitCandyNamespace? Namespace { get; set; }

    public GitCandyNamespaceAlias? NamespaceAlias { get; set; }
}

/// <summary>同一 namespace 内当前仓库和历史仓库名共享的名称占用。</summary>
public sealed class GitCandyRepositoryClaim
{
    public long NamespaceId { get; set; }

    public string NormalizedSlug { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public NameClaimType ClaimType { get; set; }

    public long? RepositoryId { get; set; }

    public long? RepositoryAliasId { get; set; }

    public GitCandyNamespace? Namespace { get; set; }

    public GitCandyRepository? Repository { get; set; }

    public GitCandyRepositoryAlias? RepositoryAlias { get; set; }
}

/// <summary>名称占用来源。</summary>
public enum NameClaimType
{
    Current = 1,
    Alias = 2,
    Reserved = 3
}
