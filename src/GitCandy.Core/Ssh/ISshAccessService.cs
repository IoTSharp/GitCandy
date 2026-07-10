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
    /// <param name="recordUsage">是否在签名验证成功后记录本次 key 使用时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>认证用户；认证失败时为 <see langword="null" />。</returns>
    Task<SshPrincipal?> AuthenticateAsync(
        string keyType,
        byte[] publicKey,
        bool recordUsage = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用 OpenSSH SHA-256 fingerprint 解析已由 sshd 验证的 public key 和 Identity 用户。
    /// </summary>
    /// <param name="fingerprint">带或不带 <c>SHA256:</c> 前缀的 fingerprint。</param>
    /// <param name="recordUsage">是否记录本次 key 使用时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已登记 key；不存在时为 <see langword="null" />。</returns>
    Task<SshAuthorizedKey?> FindAuthorizedKeyAsync(
        string fingerprint,
        bool recordUsage = false,
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
