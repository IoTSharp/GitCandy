namespace GitCandy.Releases;

/// <summary>创建 tag-backed release 的输入。</summary>
public sealed record CreateRelease(
    string TagName,
    string Name,
    string BodyMarkdown,
    bool IsDraft);

/// <summary>Release 附件元数据。</summary>
public sealed record ReleaseAsset(
    string Id,
    string FileName,
    string ContentType,
    long Length,
    string Sha256,
    long DownloadCount,
    DateTimeOffset CreatedAt);

/// <summary>Release 列表与详情投影。</summary>
public sealed record ReleaseDetails(
    long Id,
    long RepositoryId,
    string TagName,
    string TagCommitSha,
    string Name,
    string BodyMarkdown,
    string BodyHtml,
    bool IsDraft,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>附件下载流；调用方负责释放 Content。</summary>
public sealed record ReleaseAssetDownload(ReleaseAsset Asset, Stream Content);

/// <summary>附件存储完成后的校验结果。</summary>
public sealed record StoredReleaseAsset(long Length, string Sha256);

/// <summary>解析真实 Git tag 到 commit 的边界。</summary>
public interface IReleaseTagResolver
{
    string? ResolveTag(
        string repositoryStorageName,
        string tagName,
        CancellationToken cancellationToken = default);
}

/// <summary>Release 附件的流式、安全路径存储边界。</summary>
public interface IReleaseAssetStore
{
    Task<StoredReleaseAsset?> StoreAsync(
        long repositoryId,
        long releaseId,
        string assetId,
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        long repositoryId,
        long releaseId,
        string assetId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(long repositoryId, long releaseId, string assetId, CancellationToken cancellationToken = default);

    Task<int> DeleteOrphansAsync(
        IReadOnlySet<string> activeAssetIds,
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);
}

/// <summary>Release、附件权限无关的应用服务边界；授权由 Web 边界复核。</summary>
public interface IReleaseService
{
    Task<IReadOnlyList<ReleaseDetails>> GetReleasesAsync(
        long repositoryId,
        bool includeDrafts,
        CancellationToken cancellationToken = default);

    Task<ReleaseDetails?> GetReleaseAsync(
        long repositoryId,
        long releaseId,
        bool includeDrafts,
        CancellationToken cancellationToken = default);

    Task<ReleaseDetails?> CreateAsync(
        long repositoryId,
        string actorUserId,
        CreateRelease command,
        CancellationToken cancellationToken = default);

    Task<ReleaseAsset?> AddAssetAsync(
        long repositoryId,
        long releaseId,
        string actorUserId,
        string fileName,
        string contentType,
        Stream content,
        long? declaredLength,
        CancellationToken cancellationToken = default);

    Task<ReleaseAssetDownload?> OpenAssetAsync(
        long repositoryId,
        string assetId,
        bool includeDrafts,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAssetAsync(
        long repositoryId,
        string assetId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<int> CleanupOrphansAsync(CancellationToken cancellationToken = default);
}
