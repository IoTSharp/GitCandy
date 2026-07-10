namespace GitCandy.Application;

/// <summary>
/// 仓库列表和权限检查场景使用的轻量摘要。
/// </summary>
/// <param name="Name">仓库名称。</param>
/// <param name="NormalizedName">规范化仓库名称。</param>
/// <param name="Description">仓库描述。</param>
/// <param name="IsPrivate">仓库是否为私有仓库。</param>
/// <param name="AllowAnonymousRead">是否允许匿名读取。</param>
/// <param name="AllowAnonymousWrite">是否允许匿名写入。</param>
/// <param name="CreatedAtUtc">仓库创建时间。</param>
public sealed record RepositorySummary(
    string Name,
    string NormalizedName,
    string Description,
    bool IsPrivate,
    bool AllowAnonymousRead,
    bool AllowAnonymousWrite,
    DateTime CreatedAtUtc);
