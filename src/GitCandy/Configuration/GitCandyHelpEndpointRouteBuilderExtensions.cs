using GitCandy.Help;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GitCandy.Configuration;

/// <summary>
/// 注册构建阶段生成的 GitCandy 帮助站点。
/// </summary>
public static class GitCandyHelpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// 注册匿名只读的 <c>/help</c> 静态文档端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>同一个端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapGitCandyHelp(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapMethods("/help", [HttpMethods.Get, HttpMethods.Head], HelpContentEndpoint.ServeRootAsync)
            .AllowAnonymous();
        endpoints.MapMethods("/help/{**path}", [HttpMethods.Get, HttpMethods.Head], HelpContentEndpoint.ServeAsync)
            .AllowAnonymous();
        return endpoints;
    }
}
