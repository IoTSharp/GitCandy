namespace GitCandy.Authorization;

/// <summary>
/// 仓库授权资源。
/// </summary>
/// <param name="RepositoryName">仓库名称。</param>
public sealed record RepositoryAuthorizationResource
{
    /// <summary>使用旧名称兼容入口创建授权资源。</summary>
    public RepositoryAuthorizationResource(string repositoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        RepositoryName = repositoryName;
    }

    /// <summary>使用稳定仓库 ID 创建授权资源。</summary>
    public RepositoryAuthorizationResource(long repositoryId)
    {
        RepositoryId = repositoryId;
    }

    /// <summary>稳定仓库 ID。</summary>
    public long? RepositoryId { get; }

    /// <summary>迁移期旧仓库名称。</summary>
    public string? RepositoryName { get; }
}
