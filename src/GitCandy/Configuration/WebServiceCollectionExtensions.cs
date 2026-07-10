using System.Globalization;
using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Caching;
using GitCandy.Data;
using GitCandy.Data.Configuration;
using GitCandy.Data.Identity;
using GitCandy.Data.Sqlite;
using GitCandy.Diagnostics;
using GitCandy.Git;
using GitCandy.Operations;
using GitCandy.Profiling;
using GitCandy.Schedules;
using GitCandy.Ssh;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
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
    /// 注册 ASP.NET Core MVC、Identity、授权和本地化配置。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <param name="contentRootPath">应用 content root。省略时使用应用基目录，主要供不启动 Web host 的测试使用。</param>
    /// <returns>同一个服务集合。</returns>
    public static IServiceCollection AddGitCandyWebShell(
        this IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddControllersWithViews()
            .AddViewLocalization();
        services.AddHttpContextAccessor();
        services.AddGitCandyApplicationOptions(configuration);
        services.AddDataProtection()
            .SetApplicationName("GitCandy")
            .PersistKeysToFileSystem(new DirectoryInfo(
                ResolveDataProtectionKeysPath(configuration, contentRootPath)));
        services.AddGitSmartHttpOptions(configuration);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, GitCandyHostDiagnosticsHostedService>());
        services.TryAddSingleton<IGitCandyApplicationPaths, GitCandyApplicationPaths>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, GitCandyApplicationPathValidationHostedService>());
        services.TryAddSingleton<IRequestProfilerAccessor, HttpContextRequestProfilerAccessor>();

        services.AddGitCandyData(configuration, builder => builder.AddSqlite());
        services.AddGitCandyHealthChecks();

        services.AddIdentity<GitCandyUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<GitCandyDbContext>()
            .AddDefaultTokenProviders();

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, GitBasicAuthenticationHandler>(
                GitCandyAuthenticationSchemes.GitBasic,
                _ => { });

        services.AddGitCandyMigrationServices();
        services.AddGitCandyScheduler();
        services.AddGitCandySsh();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = ".GitCandy.Identity";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.Administrator,
                policy => policy.RequireAuthenticatedUser()
                    .RequireRole(RoleNames.Administrator));
            options.AddPolicy(
                AuthorizationPolicies.RepositoryRead,
                policy => policy.AddRequirements(
                    new RepositoryAuthorizationRequirement(RepositoryPermission.Read)));
            options.AddPolicy(
                AuthorizationPolicies.RepositoryWrite,
                policy => policy.AddRequirements(
                    new RepositoryAuthorizationRequirement(RepositoryPermission.Write)));
            options.AddPolicy(
                AuthorizationPolicies.RepositoryOwner,
                policy => policy.RequireAuthenticatedUser()
                    .AddRequirements(
                        new RepositoryAuthorizationRequirement(RepositoryPermission.Owner)));
            options.AddPolicy(
                AuthorizationPolicies.TeamAdministrator,
                policy => policy.RequireAuthenticatedUser()
                    .AddRequirements(new TeamAdministratorRequirement()));
            options.AddPolicy(
                AuthorizationPolicies.CurrentUser,
                policy => policy.RequireAuthenticatedUser()
                    .AddRequirements(new CurrentUserRequirement()));
        });

        services.AddMemoryCache();
        services.TryAddSingleton<IApplicationCache, MemoryApplicationCache>();

        services.AddLocalization(options => options.ResourcesPath = "Resources");
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
        services.TryAddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.TryAddScoped<IMembershipService, MembershipService>();
        services.TryAddScoped<IUserAdministrationService, UserAdministrationService>();
        services.TryAddScoped<ITeamService, TeamService>();
        services.TryAddScoped<IRepositoryService, RepositoryService>();
        services.TryAddScoped<IRepositoryManagementService, RepositoryManagementService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IAuthorizationHandler, RepositoryAuthorizationHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IAuthorizationHandler, TeamAdministratorAuthorizationHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IAuthorizationHandler, CurrentUserAuthorizationHandler>());
        services.TryAddSingleton<IGitRepositoryPathResolver, GitRepositoryPathResolver>();
        services.TryAddScoped<IGitServiceFactory, GitServiceFactory>();
        services.TryAddSingleton<IGitTransportBackend, GitProcessTransportBackend>();
        services.TryAddSingleton<IGitExecutableResolver, GitExecutableResolver>();
        return services;
    }

    private static IServiceCollection AddGitCandyScheduler(this IServiceCollection services)
    {
        services.TryAddTransient<QuartzSchedulerJob>();

        services.AddQuartz(options =>
        {
            options.SchedulerId = "GitCandy";
            options.SchedulerName = "GitCandy Scheduler";
            options.InterruptJobsOnShutdown = true;
            options.InterruptJobsOnShutdownWithWait = true;
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
        services.TryAddSingleton<ISshHostKeyProvider, FileSshHostKeyProvider>();
        services.TryAddSingleton<ISshAccessService, SshAccessService>();
        services.TryAddSingleton<ISshServerRuntime, BuiltInSshServerRuntime>();
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

    private static string ResolveDataProtectionKeysPath(
        IConfiguration configuration,
        string? contentRootPath)
    {
        var configuredPath = configuration[$"{GitCandyApplicationOptions.SectionName}:{nameof(GitCandyApplicationOptions.DataProtectionKeysPath)}"]
            ?? "App_Data/DataProtectionKeys";
        var path = configuredPath.Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (path.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        var rootPath = Path.GetFullPath(contentRootPath ?? AppContext.BaseDirectory);
        var fullPath = Path.GetFullPath(path, rootPath);
        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        if (relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException(
                "The Data Protection key path must stay inside the content root when configured as a relative path.");
        }

        return fullPath;
    }

    private static IServiceCollection AddGitSmartHttpOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<GitSmartHttpOptions>()
            .Bind(configuration.GetSection(GitSmartHttpOptions.SectionName))
            .Validate(
                options => options.MaxRequestBodySize > 0,
                $"{nameof(GitSmartHttpOptions.MaxRequestBodySize)} must be greater than zero.")
            .Validate(
                options => options.RequestTimeout > TimeSpan.Zero,
                $"{nameof(GitSmartHttpOptions.RequestTimeout)} must be greater than zero.")
            .Validate(
                options => options.StreamBufferSize >= 4096,
                $"{nameof(GitSmartHttpOptions.StreamBufferSize)} must be at least 4096 bytes.")
            .Validate(
                options => options.MaxConcurrentOperations is >= 1 and <= 1024,
                $"{nameof(GitSmartHttpOptions.MaxConcurrentOperations)} must be between 1 and 1024.")
            .ValidateOnStart();

        return services;
    }
}
