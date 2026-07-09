namespace GitCandy.Schedules;

/// <summary>
/// 后台调度任务类型。
/// </summary>
public enum SchedulerJobType
{
    /// <summary>
    /// 延迟较低的轻量任务。
    /// </summary>
    RealTime,

    /// <summary>
    /// 可能耗时较长的后台任务。
    /// </summary>
    LongRunning
}
