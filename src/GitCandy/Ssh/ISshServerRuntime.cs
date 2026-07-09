namespace GitCandy.Ssh;

/// <summary>
/// 内置 SSH server 的生命周期入口。
/// </summary>
public interface ISshServerRuntime
{
    /// <summary>
    /// 启动内置 SSH server。
    /// </summary>
    /// <param name="port">监听端口。</param>
    /// <param name="cancellationToken">应用启动取消令牌。</param>
    /// <returns>异步启动任务。</returns>
    Task StartAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止内置 SSH server。
    /// </summary>
    /// <param name="cancellationToken">应用停止取消令牌。</param>
    /// <returns>异步停止任务。</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}
