namespace GitCandy.Git;

/// <summary>
/// Git helper 启动或执行失败。
/// </summary>
public sealed class GitTransportException : Exception
{
    /// <summary>
    /// 使用不包含敏感路径或 helper stderr 的安全消息创建异常。
    /// </summary>
    /// <param name="message">安全错误消息。</param>
    /// <param name="innerException">内部异常。</param>
    public GitTransportException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
