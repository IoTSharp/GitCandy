namespace GitCandy.Profiling;

/// <summary>
/// 提供当前 HTTP 请求 profiler 的访问入口。
/// </summary>
public interface IRequestProfilerAccessor
{
    /// <summary>
    /// 获取当前请求的 profiler；当请求尚未进入 profiler middleware 时为 <see langword="null"/>。
    /// </summary>
    RequestProfiler? Current { get; }
}
