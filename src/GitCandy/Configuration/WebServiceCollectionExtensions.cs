using System.Globalization;
using GitCandy.Data;
using GitCandy.Data.Configuration;
using GitCandy.Data.Identity;
using GitCandy.Data.Sqlite;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Configuration;

/// <summary>
/// ASP.NET Core host 服务注册扩展。
/// </summary>
public static class WebServiceCollectionExtensions
{
    private static readonly CultureInfo[] SupportedCultures =
    [
        CultureInfo.GetCultureInfo("en-US"),
        CultureInfo.GetCultureInfo("zh-Hans"),
        CultureInfo.GetCultureInfo("fr-FR")
    ];

    /// <summary>
    /// 注册 ASP.NET Core MVC 外壳、Identity、授权、Session 和本地化占位配置。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <returns>同一个服务集合。</returns>
    public static IServiceCollection AddGitCandyWebShell(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddControllersWithViews();

        services.AddGitCandyData(configuration, builder => builder.AddSqlite());

        services.AddIdentity<GitCandyUser, IdentityRole>()
            .AddEntityFrameworkStores<GitCandyDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = ".GitCandy.Identity";
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.SlidingExpiration = true;
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.Administrator,
                policy => policy.RequireAuthenticatedUser()
                    .RequireRole(RoleNames.Administrator));
        });

        services.AddDistributedMemoryCache();
        services.AddSession(options =>
        {
            options.Cookie.Name = ".GitCandy.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        services.AddLocalization();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture("en-US");
            options.SupportedCultures = SupportedCultures;
            options.SupportedUICultures = SupportedCultures;
        });

        return services;
    }
}
