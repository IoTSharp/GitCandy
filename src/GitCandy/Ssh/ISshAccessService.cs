namespace GitCandy.Ssh;

/// <summary>
/// SSH public key 认证和仓库授权边界。
/// </summary>
public interface ISshAccessService
{
    /// <summary>
    /// 使用 SSH public key blob 解析 Identity 用户。
    /// </summary>
    /// <param name="keyType">客户端声明的 SSH key 算法。</param>
    /// <param name="publicKey">SSH wire 格式 public key blob。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>认证用户；认证失败时为 <see langword="null" />。</returns>
    Task<SshPrincipal?> AuthenticateAsync(
        string keyType,
        byte[] publicKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断已认证 SSH 用户能否执行指定仓库操作。
    /// </summary>
    /// <param name="principal">已认证用户。</param>
    /// <param name="repositoryName">仓库名称。</param>
    /// <param name="requiresWrite">是否要求写权限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> CanAccessRepositoryAsync(
        SshPrincipal principal,
        string repositoryName,
        bool requiresWrite,
        CancellationToken cancellationToken = default);
}
