using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GitCandy.Configuration;

/// <summary>
/// GitCandy ASP.NET Core 端点路由注册扩展。
/// </summary>
public static class GitCandyEndpointRouteBuilderExtensions
{
    /// <summary>
    /// 注册迁移期兼容占位路由，先保持旧 GitCandy 公开 URL 可匹配。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>同一个端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapGitCandyCompatibilityRoutes(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapControllerRoute(
            name: "git-dotgit",
            pattern: "git/{project}.git/{**verb}",
            defaults: new { controller = "Compatibility", action = "Git" });

        endpoints.MapControllerRoute(
            name: "git",
            pattern: "git/{project}/{**verb}",
            defaults: new { controller = "Compatibility", action = "Git" });

        endpoints.MapControllerRoute(
            name: "account",
            pattern: "Account/{action}/{name?}",
            defaults: new { controller = "Account" });

        endpoints.MapControllerRoute(
            name: "team",
            pattern: "Team/{action=Index}/{name?}",
            defaults: new { controller = "Team" });

        endpoints.MapControllerRoute(
            name: "repository",
            pattern: "Repository/{action=Index}/{name?}/{**path}",
            defaults: new { controller = "Repository" });

        endpoints.MapControllerRoute(
            name: "setting",
            pattern: "Setting/{action=Edit}",
            defaults: new { controller = "Setting" });

        endpoints.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
            .WithStaticAssets();

        return endpoints;
    }
}
