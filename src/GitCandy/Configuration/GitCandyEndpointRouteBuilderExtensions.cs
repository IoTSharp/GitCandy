using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GitCandy.Configuration;

/// <summary>
/// GitCandy ASP.NET Core 端点路由注册扩展。
/// </summary>
public static class GitCandyEndpointRouteBuilderExtensions
{
    /// <summary>
    /// 注册 Git Smart HTTP 与 MVC 路由。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>同一个端点路由构建器。</returns>
    public static IEndpointRouteBuilder MapGitCandyCompatibilityRoutes(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapControllers();

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

        endpoints.MapControllerRoute(
            name: "public-profile",
            pattern: "{username}",
            defaults: new { controller = "PublicProfile", action = "Index" });

        endpoints.MapControllerRoute(
            name: "namespace-repository-dotgit",
            pattern: "{namespaceSlug}/{project}.git",
            defaults: new { controller = "NamespaceRepository", action = "GitCompatibility" });

        endpoints.MapControllerRoute(
            name: "namespace-repository",
            pattern: "{namespaceSlug}/{project}",
            defaults: new { controller = "NamespaceRepository", action = "Index" });

        endpoints.MapControllerRoute(
            name: "namespace-git-dotgit",
            pattern: "{namespaceSlug}/{project}.git/{**verb}",
            defaults: new { controller = "Git", action = "Smart" });

        return endpoints;
    }
}
