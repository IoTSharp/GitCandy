using Microsoft.AspNetCore.Builder;

namespace GitCandy.Profiling;

/// <summary>
/// GitCandy 请求 profiler middleware 注册扩展。
/// </summary>
public static class GitCandyRequestProfilerApplicationBuilderExtensions
{
    /// <summary>
    /// 在 ASP.NET Core 请求管线中启用 GitCandy 请求 profiler。
    /// </summary>
    /// <param name="app">应用程序构建器。</param>
    /// <returns>同一个应用程序构建器。</returns>
    public static IApplicationBuilder UseGitCandyRequestProfiler(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<GitCandyRequestProfilerMiddleware>();
    }
}
