namespace GitCandy.Ssh;

/// <summary>
/// 通过 SSH public key 认证的 GitCandy 用户。
/// </summary>
/// <param name="UserId">Identity 用户主键。</param>
/// <param name="UserName">Identity 用户名。</param>
/// <param name="IsAdministrator">是否属于 GitCandy 管理员角色。</param>
public sealed record SshPrincipal(string UserId, string UserName, bool IsAdministrator);
