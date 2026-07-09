using GitCandy.Profiling;
using Microsoft.AspNetCore.Http;

namespace GitCandy.Tests;

[TestClass]
public sealed class RequestProfilerMiddlewareTests
{
    [TestMethod]
    public async Task InvokeAsync_WithHttpRequest_StartsProfilerBeforeNextMiddleware()
    {
        var httpContext = new DefaultHttpContext();
        RequestProfiler? profilerDuringNext = null;
        var middleware = new GitCandyRequestProfilerMiddleware(context =>
        {
            profilerDuringNext = context.GetGitCandyRequestProfiler();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext);

        Assert.IsNotNull(profilerDuringNext);
        Assert.AreSame(profilerDuringNext, httpContext.GetGitCandyRequestProfiler());
        Assert.IsTrue(profilerDuringNext.Elapsed >= TimeSpan.Zero);
    }

    [TestMethod]
    public void Current_WithMiddlewareProfiler_ReturnsProfilerFromHttpContextItems()
    {
        var httpContext = new DefaultHttpContext();
        var profiler = RequestProfiler.StartNew();
        httpContext.SetGitCandyRequestProfiler(profiler);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = httpContext
        };
        var accessor = new HttpContextRequestProfilerAccessor(httpContextAccessor);

        Assert.AreSame(profiler, accessor.Current);
    }
}
