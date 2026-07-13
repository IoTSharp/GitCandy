namespace GitCandy.Search;

/// <summary>协作搜索支持的资源范围。</summary>
public enum SearchScope
{
    All,
    Repository,
    Issue,
    PullRequest,
    Commit,
    Code
}

/// <summary>已完成权限过滤、可交给 Git 内容搜索的仓库候选。</summary>
public sealed record SearchRepositoryCandidate(
    long RepositoryId,
    string NamespaceSlug,
    string RepositorySlug,
    string StorageName,
    string Description);

/// <summary>统一协作搜索命中。</summary>
public sealed record SearchHit(
    SearchScope Scope,
    long RepositoryId,
    string Repository,
    string Title,
    string Excerpt,
    string Url,
    DateTimeOffset? UpdatedAt = null);

/// <summary>数据库搜索结果及其权限过滤后的 Git 候选。</summary>
public sealed record DatabaseSearchResult(
    IReadOnlyList<SearchHit> Hits,
    IReadOnlyList<SearchRepositoryCandidate> Repositories);

/// <summary>仓库、Issue 与 Pull Request 的权限优先搜索边界。</summary>
public interface ICollaborationSearchService
{
    Task<DatabaseSearchResult> SearchAsync(
        string? userId,
        bool isAdministrator,
        string query,
        SearchScope scope,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

/// <summary>在已授权仓库候选中执行有界 commit/code 搜索。</summary>
public interface IGitContentSearchService
{
    IReadOnlyList<SearchHit> Search(
        IReadOnlyList<SearchRepositoryCandidate> repositories,
        string query,
        SearchScope scope,
        int limit,
        CancellationToken cancellationToken = default);
}
