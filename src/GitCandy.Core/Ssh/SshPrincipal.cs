namespace GitCandy.Ssh;

/// <summary>
/// 通过 SSH public key 认证的 GitCandy 用户。
/// </summary>
/// <param name="UserId">Identity 用户主键；deploy key 为空。</param>
/// <param name="UserName">Identity 用户名或 deploy key 审计名称。</param>
/// <param name="IsAdministrator">是否属于 GitCandy 管理员角色。</param>
/// <param name="DeployKeyId">deploy key 主键；用户 key 为空。</param>
/// <param name="RepositoryId">deploy key 绑定仓库；用户 key 为空。</param>
/// <param name="CanWrite">deploy key 是否允许写入。</param>
public sealed record SshPrincipal(
    string? UserId,
    string UserName,
    bool IsAdministrator,
    long? DeployKeyId = null,
    long? RepositoryId = null,
    bool CanWrite = false);
