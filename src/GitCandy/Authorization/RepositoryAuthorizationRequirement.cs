using Microsoft.AspNetCore.Authorization;

namespace GitCandy.Authorization;

/// <summary>
/// 仓库资源授权要求。
/// </summary>
/// <param name="Permission">要求的仓库操作。</param>
public sealed class RepositoryAuthorizationRequirement(RepositoryPermission permission)
    : IAuthorizationRequirement
{
    /// <summary>
    /// 要求的仓库操作。
    /// </summary>
    public RepositoryPermission Permission { get; } = permission;
}
