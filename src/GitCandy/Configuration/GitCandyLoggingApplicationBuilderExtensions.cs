using GitCandy.Log;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GitCandy.Configuration;

/// <summary>
/// GitCandy ASP.NET Core 日志适配注册扩展。
/// </summary>
public static class GitCandyLoggingApplicationBuilderExtensions
{
    /// <summary>
    /// 将迁移期旧静态日志入口绑定到 ASP.NET Core 日志系统。
    /// </summary>
    /// <param name="app">ASP.NET Core 应用。</param>
    /// <returns>同一个应用实例。</returns>
    public static WebApplication ConfigureGitCandyLegacyLogger(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        Logger.Configure(loggerFactory);
        return app;
    }
}
