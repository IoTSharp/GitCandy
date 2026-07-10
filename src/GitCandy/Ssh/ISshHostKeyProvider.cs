namespace GitCandy.Ssh;

/// <summary>
/// 加载或创建持久化 SSH host key。
/// </summary>
public interface ISshHostKeyProvider
{
    /// <summary>
    /// 返回可供当前协议栈使用的 host keys。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<SshHostKey>> GetHostKeysAsync(CancellationToken cancellationToken = default);
}
