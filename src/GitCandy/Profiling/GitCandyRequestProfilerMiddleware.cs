using Microsoft.AspNetCore.Http;

namespace GitCandy.Profiling;

/// <summary>
/// 在 ASP.NET Core 请求管线入口启动 GitCandy 请求 profiler。
/// </summary>
/// <param name="next">下一个 middleware。</param>
public sealed class GitCandyRequestProfilerMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next
        ?? throw new ArgumentNullException(nameof(next));

    /// <summary>
    /// 处理当前 HTTP 请求并写入请求 profiler。
    /// </summary>
    /// <param name="httpContext">HTTP 上下文。</param>
    /// <returns>异步执行任务。</returns>
    public Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.SetGitCandyRequestProfiler(RequestProfiler.StartNew());

        return _next(httpContext);
    }
}
