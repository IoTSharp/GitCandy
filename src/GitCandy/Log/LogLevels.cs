namespace GitCandy.Log;

/// <summary>
/// 迁移期兼容旧 GitCandy Logger API 的日志级别。
/// </summary>
public enum LogLevels
{
    /// <summary>
    /// 信息日志。
    /// </summary>
    Info = 1,

    /// <summary>
    /// 警告日志。
    /// </summary>
    Warning = 2,

    /// <summary>
    /// 错误日志。
    /// </summary>
    Error = 3,
}
