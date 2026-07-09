using GitCandy.Log;
using Microsoft.Extensions.Logging;

namespace GitCandy.Schedules;

/// <summary>
/// 迁移旧每日日志路径刷新行为的调度任务。
/// </summary>
public sealed class LogRotationJob(ILogger<LogRotationJob> logger) : ISchedulerJob
{
    private readonly ILogger<LogRotationJob> _logger = logger;

    /// <inheritdoc />
    public string Name => "LegacyLogRotation";

    /// <inheritdoc />
    public SchedulerJobType JobType => SchedulerJobType.RealTime;

    /// <inheritdoc />
    public ValueTask ExecuteAsync(
        SchedulerJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        if (context.ExecutionTimes > 1)
        {
            Logger.SetLogPath();
            _logger.LogInformation("Legacy log rotation job executed.");
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public TimeSpan GetNextInterval(SchedulerJobContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var now = DateTimeOffset.Now;
        var nextLocalMidnight = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            hour: 0,
            minute: 0,
            second: 0,
            now.Offset).AddDays(1);

        return nextLocalMidnight - now;
    }
}
