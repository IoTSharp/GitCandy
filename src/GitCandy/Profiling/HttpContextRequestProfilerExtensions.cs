using Microsoft.AspNetCore.Http;

namespace GitCandy.Profiling;

/// <summary>
/// 提供在 <see cref="HttpContext"/> 上保存和读取 GitCandy 请求 profiler 的扩展方法。
/// </summary>
public static class HttpContextRequestProfilerExtensions
{
    private const string RequestProfilerItemKey = "__GitCandyRequestProfiler";

    /// <summary>
    /// 读取当前请求的 GitCandy profiler。
    /// </summary>
    /// <param name="httpContext">HTTP 上下文。</param>
    /// <returns>当前请求 profiler；未启动时为 <see langword="null"/>。</returns>
    public static RequestProfiler? GetGitCandyRequestProfiler(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.Items.TryGetValue(RequestProfilerItemKey, out var profiler)
            ? profiler as RequestProfiler
            : null;
    }

    /// <summary>
    /// 保存当前请求的 GitCandy profiler。
    /// </summary>
    /// <param name="httpContext">HTTP 上下文。</param>
    /// <param name="profiler">当前请求 profiler。</param>
    public static void SetGitCandyRequestProfiler(
        this HttpContext httpContext,
        RequestProfiler profiler)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(profiler);

        httpContext.Items[RequestProfilerItemKey] = profiler;
    }
}
