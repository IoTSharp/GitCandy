using System.Globalization;
using GitCandy.Application;
using GitCandy.Caching;
using GitCandy.Data;
using GitCandy.Data.Configuration;
using GitCandy.Data.Identity;
using GitCandy.Data.Sqlite;
using GitCandy.Git;
using GitCandy.Profiling;
using GitCandy.Schedules;
using GitCandy.Ssh;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quartz;

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
        services.AddHttpContextAccessor();
        services.AddGitCandyApplicationOptions(configuration);
        services.TryAddSingleton<IGitCandyApplicationPaths, GitCandyApplicationPaths>();
        services.TryAddSingleton<IRequestProfilerAccessor, HttpContextRequestProfilerAccessor>();

        services.AddGitCandyData(configuration, builder => builder.AddSqlite());

        services.AddIdentity<GitCandyUser, IdentityRole>()
            .AddEntityFrameworkStores<GitCandyDbContext>()
            .AddDefaultTokenProviders();

        services.AddGitCandyMigrationServices();
        services.AddGitCandyScheduler();
        services.AddGitCandySsh();

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

        services.AddMemoryCache();
        services.TryAddSingleton<IApplicationCache, MemoryApplicationCache>();
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

    private static IServiceCollection AddGitCandyMigrationServices(this IServiceCollection services)
    {
        services.TryAddScoped<IMembershipService, MembershipService>();
        services.TryAddScoped<IRepositoryService, RepositoryService>();
        services.TryAddSingleton<IGitRepositoryPathResolver, GitRepositoryPathResolver>();
        services.TryAddScoped<IGitServiceFactory, GitServiceFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISchedulerJob, LogRotationJob>());

        return services;
    }

    private static IServiceCollection AddGitCandyScheduler(this IServiceCollection services)
    {
        services.TryAddTransient<QuartzSchedulerJob>();

        services.AddQuartz(options =>
        {
            options.SchedulerId = "GitCandy";
            options.SchedulerName = "GitCandy Scheduler";
            options.UseInMemoryStore(_ => { });
            options.UseDefaultThreadPool(threadPool =>
            {
                threadPool.MaxConcurrency = Math.Max(2, Environment.ProcessorCount * 2);
            });
        });

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, QuartzSchedulerRegistrationHostedService>());

        services.AddQuartzHostedService(options =>
        {
            options.AwaitApplicationStarted = true;
            options.WaitForJobsToComplete = true;
        });

        return services;
    }

    private static IServiceCollection AddGitCandySsh(this IServiceCollection services)
    {
        services.TryAddSingleton<ISshServerRuntime, PlaceholderSshServerRuntime>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, SshServerHostedService>());

        return services;
    }

    private static IServiceCollection AddGitCandyApplicationOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<GitCandyApplicationOptions>()
            .Bind(configuration.GetSection(GitCandyApplicationOptions.SectionName))
            .ValidateOnStart();

        services.PostConfigure<GitCandyApplicationOptions>(options =>
            GitCandyApplicationOptionsConfiguration.ApplyLegacyAliases(options, configuration));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GitCandyApplicationOptions>, GitCandyApplicationOptionsValidator>());

        return services;
    }
}
