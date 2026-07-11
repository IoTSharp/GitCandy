namespace GitCandy.Git;

/// <summary>
/// Git LFS SHA-256 内容寻址对象存储。
/// </summary>
public interface IGitLfsObjectStore
{
    /// <summary>读取对象信息；不存在时返回空。</summary>
    GitLfsObjectInfo? GetInfo(string repositoryName, string oid);

    /// <summary>判断仓库配额是否允许写入对象。</summary>
    bool CanStore(string repositoryName, long size);

    /// <summary>将输入流写入临时区，校验 SHA-256 和大小后原子提交。</summary>
    Task<GitLfsObjectInfo> WriteAsync(
        string repositoryName,
        string oid,
        long? expectedSize,
        Stream input,
        CancellationToken cancellationToken = default);

    /// <summary>打开只读对象流。</summary>
    Stream OpenRead(string repositoryName, string oid);

    /// <summary>删除仓库的全部 LFS 对象。</summary>
    Task DeleteRepositoryAsync(
        string repositoryName,
        CancellationToken cancellationToken = default);
}

/// <summary>Git LFS 对象信息。</summary>
public sealed record GitLfsObjectInfo(string Oid, long Size);
