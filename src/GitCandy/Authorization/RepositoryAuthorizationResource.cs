namespace GitCandy.Authorization;

/// <summary>
/// 仓库授权资源。
/// </summary>
/// <param name="RepositoryName">仓库名称。</param>
public sealed record RepositoryAuthorizationResource(string RepositoryName);
