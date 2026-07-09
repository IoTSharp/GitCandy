using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitCandy.Log;

/// <summary>
/// 迁移期兼容旧 GitCandy 静态日志 API 的 ASP.NET Core 日志适配器。
/// </summary>
public static class Logger
{
    private static readonly EventId LegacyLoggerEventId = new(0, "LegacyLogger");
    private static ILogger _logger = NullLogger.Instance;

    /// <summary>
    /// 将旧静态日志入口绑定到当前 ASP.NET Core 宿主的日志工厂。
    /// </summary>
    /// <param name="loggerFactory">ASP.NET Core 日志工厂。</param>
    public static void Configure(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        Volatile.Write(ref _logger, loggerFactory.CreateLogger("GitCandy.LegacyLogger"));
    }

    /// <summary>
    /// 保留旧 API 形状；日志输出位置由 ASP.NET Core logging provider 统一决定。
    /// </summary>
    /// <param name="path">旧文件日志路径。迁移期仅用于兼容调用，不创建文件 sink。</param>
    public static void SetLogPath(string? path = null)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            Write(LogLevels.Info, "Legacy log file path is ignored by the ASP.NET Core logging adapter.");
        }
    }

    /// <summary>
    /// 按旧日志级别写入一条消息。
    /// </summary>
    /// <param name="level">旧日志级别。</param>
    /// <param name="message">日志消息。</param>
    public static void Write(LogLevels level, string? message)
    {
        InternalWrite(level, message);
    }

    /// <summary>
    /// 使用旧 composite format 语法写入一条消息。
    /// </summary>
    /// <param name="level">旧日志级别。</param>
    /// <param name="format">Composite format 格式字符串。</param>
    /// <param name="args">格式化参数。</param>
    public static void Write(LogLevels level, string format, params object?[] args)
    {
        InternalWrite(level, FormatLegacyMessage(format, args));
    }

    /// <summary>
    /// 写入信息日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public static void Info(string? message)
    {
        InternalWrite(LogLevels.Info, message);
    }

    /// <summary>
    /// 使用旧 composite format 语法写入信息日志。
    /// </summary>
    /// <param name="format">Composite format 格式字符串。</param>
    /// <param name="args">格式化参数。</param>
    public static void Info(string format, params object?[] args)
    {
        InternalWrite(LogLevels.Info, FormatLegacyMessage(format, args));
    }

    /// <summary>
    /// 写入警告日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public static void Warning(string? message)
    {
        InternalWrite(LogLevels.Warning, message);
    }

    /// <summary>
    /// 使用旧 composite format 语法写入警告日志。
    /// </summary>
    /// <param name="format">Composite format 格式字符串。</param>
    /// <param name="args">格式化参数。</param>
    public static void Warning(string format, params object?[] args)
    {
        InternalWrite(LogLevels.Warning, FormatLegacyMessage(format, args));
    }

    /// <summary>
    /// 写入错误日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public static void Error(string? message)
    {
        InternalWrite(LogLevels.Error, message);
    }

    /// <summary>
    /// 使用旧 composite format 语法写入错误日志。
    /// </summary>
    /// <param name="format">Composite format 格式字符串。</param>
    /// <param name="args">格式化参数。</param>
    public static void Error(string format, params object?[] args)
    {
        InternalWrite(LogLevels.Error, FormatLegacyMessage(format, args));
    }

    private static void InternalWrite(LogLevels level, string? message)
    {
        string safeMessage = (message ?? string.Empty).Trim();
        ILogger logger = Volatile.Read(ref _logger);
        logger.Log(
            MapLogLevel(level),
            LegacyLoggerEventId,
            safeMessage,
            exception: null,
            static (state, _) => state);
    }

    private static string FormatLegacyMessage(string format, object?[] args)
    {
        ArgumentNullException.ThrowIfNull(format);

        return args.Length == 0
            ? format
            : string.Format(CultureInfo.CurrentCulture, format, args);
    }

    private static LogLevel MapLogLevel(LogLevels level)
    {
        return level switch
        {
            LogLevels.Warning => LogLevel.Warning,
            LogLevels.Error => LogLevel.Error,
            _ => LogLevel.Information,
        };
    }
}
