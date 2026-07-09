namespace GitCandy.Schedules;

/// <summary>
/// ASP.NET Core DI 管理的后台调度任务入口。
/// </summary>
public interface ISchedulerJob
{
    /// <summary>
    /// 任务名称，用于日志和诊断。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 任务类型。
    /// </summary>
    SchedulerJobType JobType { get; }

    /// <summary>
    /// 执行任务。
    /// </summary>
    /// <param name="context">调度上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步完成标记。</returns>
    ValueTask ExecuteAsync(
        SchedulerJobContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算下一次执行间隔。
    /// </summary>
    /// <param name="context">调度上下文。</param>
    /// <returns>下一次执行间隔；返回 <see cref="TimeSpan.MaxValue" /> 表示不再调度。</returns>
    TimeSpan GetNextInterval(SchedulerJobContext context);
}
