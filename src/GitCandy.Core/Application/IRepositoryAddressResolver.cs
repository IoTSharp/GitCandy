namespace GitCandy.Application;

/// <summary>
/// 统一解析 Web、Git HTTP 和 SSH 使用的稳定仓库地址。
/// </summary>
public interface IRepositoryAddressResolver
{
    /// <summary>按 namespace/repository 地址解析稳定仓库。</summary>
    Task<RepositoryAddressResolution?> ResolveAsync(
        string namespaceSlug,
        string repositorySlug,
        CancellationToken cancellationToken = default);

    /// <summary>按旧 <c>/git/{project}</c> 映射解析稳定仓库。</summary>
    Task<RepositoryAddressResolution?> ResolveLegacyAsync(
        string project,
        CancellationToken cancellationToken = default);
}

/// <summary>稳定仓库地址解析结果。</summary>
public sealed record RepositoryAddressResolution(
    long RepositoryId,
    long NamespaceId,
    string NamespaceSlug,
    string RepositorySlug,
    string StorageName,
    bool IsPrivate,
    bool UsedNamespaceAlias,
    bool UsedRepositoryAlias,
    bool UsedLegacyRoute)
{
    /// <summary>规范 Web 仓库路径。</summary>
    public string CanonicalPath => $"/{Uri.EscapeDataString(NamespaceSlug)}/{Uri.EscapeDataString(RepositorySlug)}";

    /// <summary>请求是否使用了历史地址。</summary>
    public bool UsedAlias => UsedNamespaceAlias || UsedRepositoryAlias || UsedLegacyRoute;
}
