namespace GitCandy.Application;

/// <summary>
/// 仓库列表和权限检查场景使用的轻量摘要。
/// </summary>
/// <param name="Id">稳定仓库 ID。</param>
/// <param name="NamespaceId">稳定 namespace ID。</param>
/// <param name="NamespaceSlug">当前 namespace slug。</param>
/// <param name="Name">仓库名称。</param>
/// <param name="NormalizedName">规范化仓库名称。</param>
/// <param name="Description">仓库描述。</param>
/// <param name="IsPrivate">仓库是否为私有仓库。</param>
/// <param name="AllowAnonymousRead">是否允许匿名读取。</param>
/// <param name="AllowAnonymousWrite">是否允许匿名写入。</param>
/// <param name="CreatedAtUtc">仓库创建时间。</param>
public sealed record RepositorySummary(
    long Id,
    long NamespaceId,
    string NamespaceSlug,
    string Name,
    string NormalizedName,
    string StorageName,
    string Description,
    bool IsPrivate,
    bool AllowAnonymousRead,
    bool AllowAnonymousWrite,
    DateTime CreatedAtUtc);
