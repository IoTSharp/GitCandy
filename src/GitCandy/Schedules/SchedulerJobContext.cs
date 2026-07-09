namespace GitCandy.Schedules;

/// <summary>
/// 调度任务执行上下文。
/// </summary>
/// <param name="ExecutionTimes">任务已执行次数。</param>
/// <param name="UtcCreation">任务创建时间。</param>
/// <param name="UtcLastStart">任务上一次开始时间。</param>
/// <param name="UtcLastEnd">任务上一次结束时间。</param>
public sealed record SchedulerJobContext(
    int ExecutionTimes,
    DateTimeOffset UtcCreation,
    DateTimeOffset? UtcLastStart,
    DateTimeOffset? UtcLastEnd);
