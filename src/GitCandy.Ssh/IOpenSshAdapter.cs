namespace GitCandy.Ssh;

/// <summary>
/// 外部 OpenSSH AuthorizedKeysCommand 和 forced-command 适配入口。
/// </summary>
public interface IOpenSshAdapter
{
    /// <summary>
    /// 为 sshd 输出带最小权限限制和 forced command 的 authorized_keys 记录。
    /// </summary>
    Task<int> WriteAuthorizedKeyAsync(
        string fingerprint,
        TextWriter output,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 对 sshd 提供的原始 Git 命令执行认证、授权和共享 transport backend。
    /// </summary>
    Task<int> ExecuteForcedCommandAsync(
        string fingerprint,
        string? originalCommand,
        string? gitProtocol,
        Stream input,
        Stream output,
        TextWriter error,
        CancellationToken cancellationToken = default);
}
