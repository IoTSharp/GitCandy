using System.Diagnostics;

namespace GitCandy.Profiling;

/// <summary>
/// 表示单个 HTTP 请求的轻量耗时探针。
/// </summary>
public sealed class RequestProfiler
{
    private readonly Stopwatch _stopwatch;

    private RequestProfiler(Stopwatch stopwatch)
    {
        _stopwatch = stopwatch;
    }

    /// <summary>
    /// 获取请求从进入 GitCandy profiler middleware 后经过的时间。
    /// </summary>
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    /// <summary>
    /// 启动一个新的请求探针。
    /// </summary>
    /// <returns>已经开始计时的请求探针。</returns>
    public static RequestProfiler StartNew()
    {
        return new RequestProfiler(Stopwatch.StartNew());
    }
}
