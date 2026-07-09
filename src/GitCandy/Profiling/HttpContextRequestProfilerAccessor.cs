using Microsoft.AspNetCore.Http;

namespace GitCandy.Profiling;

/// <summary>
/// 基于 <see cref="HttpContext.Items"/> 读取当前请求 profiler。
/// </summary>
/// <param name="httpContextAccessor">ASP.NET Core HTTP 上下文访问器。</param>
public sealed class HttpContextRequestProfilerAccessor(
    IHttpContextAccessor httpContextAccessor) : IRequestProfilerAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor
        ?? throw new ArgumentNullException(nameof(httpContextAccessor));

    /// <inheritdoc />
    public RequestProfiler? Current =>
        _httpContextAccessor.HttpContext?.GetGitCandyRequestProfiler();
}
