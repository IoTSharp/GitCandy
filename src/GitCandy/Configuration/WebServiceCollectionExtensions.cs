using System.Globalization;
using System.Net;
using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Caching;
using GitCandy.Data;
using GitCandy.Data.Configuration;
using GitCandy.Data.Identity;
using GitCandy.Data.SonnetDB;
using GitCandy.Data.Sqlite;
using GitCandy.Diagnostics;
using GitCandy.Git;
using GitCandy.Identity;
using GitCandy.IdentityServices;
using GitCandy.Operations;
using GitCandy.Profiling;
using GitCandy.Schedules;
using GitCandy.Ssh;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.HttpOverrides;
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
        services.AddGitCandyNamespaceOptions(configuration);
        services.AddGitCandyOpenSshOptions(configuration);
        services.AddGitCandyProxyOptions(configuration);
        services.AddDataProtection()
            .SetApplicationName("GitCandy")
            .PersistKeysToFileSystem(new DirectoryInfo(
                ResolveDataProtectionKeysPath(configuration, contentRootPath)));
        services.AddGitSmartHttpOptions(configuration);
        services.AddRepositoryBrowserOptions(configuration);
        var identitySettings = AddGitCandyIdentityOptions(services, configuration);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, GitCandyHostDiagnosticsHostedService>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, NamespaceAliasCleanupHostedService>());
        services.TryAddSingleton<IGitCandyApplicationPaths, GitCandyApplicationPaths>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, GitCandyApplicationPathValidationHostedService>());
        services.TryAddSingleton<IRequestProfilerAccessor, HttpContextRequestProfilerAccessor>();

        services.AddConfiguredGitCandyData(configuration);
        services.AddGitCandyApplicationServices();
        services.AddGitCandyGit();
        services.AddGitCandyHealthChecks();

        services.AddIdentity<GitCandyUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = identitySettings.Password.RequiredLength;
                options.Password.RequiredUniqueChars = identitySettings.Password.RequiredUniqueChars;
                options.Password.RequireDigit = identitySettings.Password.RequireDigit;
                options.Password.RequireLowercase = identitySettings.Password.RequireLowercase;
                options.Password.RequireUppercase = identitySettings.Password.RequireUppercase;
                options.Password.RequireNonAlphanumeric = identitySettings.Password.RequireNonAlphanumeric;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<GitCandyDbContext>()
            .AddDefaultTokenProviders();
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = identitySettings.AccountRecovery.TokenLifespan);

        var authentication = services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, GitBasicAuthenticationHandler>(
                GitCandyAuthenticationSchemes.GitBasic,
                _ => { });

        if (identitySettings.OpenIdConnect.Enabled)
        {
            var openIdConnect = identitySettings.OpenIdConnect;
            authentication.AddOpenIdConnect(
                GitCandyAuthenticationSchemes.OpenIdConnect,
                openIdConnect.DisplayName,
                options =>
                {
                    options.SignInScheme = IdentityConstants.ExternalScheme;
                    options.Authority = openIdConnect.Authority;
                    options.ClientId = openIdConnect.ClientId;
                    options.ClientSecret = openIdConnect.ClientSecret;
                    options.CallbackPath = openIdConnect.CallbackPath;
                    options.RequireHttpsMetadata = openIdConnect.RequireHttpsMetadata;
                    options.ResponseType = "code";
                    options.UsePkce = true;
                    options.SaveTokens = false;
                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.Scope.Add("email");
                });
        }

        services.AddGitCandyWebServices();
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

    private static IServiceCollection AddGitCandyWebServices(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAccountRecoveryThrottle, AccountRecoveryThrottle>();
        services.TryAddScoped<IAccountEmailSender, SmtpAccountEmailSender>();
        services.TryAddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IAuthorizationHandler, RepositoryAuthorizationHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IAuthorizationHandler, TeamAdministratorAuthorizationHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IAuthorizationHandler, CurrentUserAuthorizationHandler>());
        return services;
    }

    /// <summary>
    /// 注册 OpenSSH 短生命周期命令所需的最小数据、路径和 Git transport 服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <returns>同一个服务集合。</returns>
    public static IServiceCollection AddGitCandyOpenSshCommand(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddGitCandyApplicationOptions(configuration);
        services.AddGitCandyNamespaceOptions(configuration);
        services.AddGitCandyOpenSshOptions(configuration);
        services.AddGitSmartHttpOptions(configuration);
        services.TryAddSingleton<IGitCandyApplicationPaths, GitCandyApplicationPaths>();
        services.AddConfiguredGitCandyData(configuration);
        services.AddGitCandyApplicationServices();
        services.AddGitCandyGit();
        services.AddGitCandyOpenSshAdapter();
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

    private static IServiceCollection AddConfiguredGitCandyData(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = GitCandyDatabaseOptionsReader.Read(configuration).Provider;
        return services.AddGitCandyData(configuration, builder =>
        {
            switch (provider)
            {
                case GitCandyDatabaseProvider.Sqlite:
                    builder.AddSqlite();
                    break;
                case GitCandyDatabaseProvider.SonnetDB:
                    builder.AddSonnetDB();
                    break;
                default:
                    throw new NotSupportedException(
                        $"GitCandy database provider '{provider}' is not available in this host.");
            }
        });
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

    private static IServiceCollection AddGitCandyOpenSshOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<GitCandyOpenSshOptions>()
            .Bind(configuration.GetSection(GitCandyOpenSshOptions.SectionName))
            .Validate(
                options => !options.Enabled
                    || (Path.IsPathFullyQualified(options.ExecutablePath)
                        && !options.ExecutablePath.Contains('\r')
                        && !options.ExecutablePath.Contains('\n')),
                "Enabled OpenSSH adaptation requires an absolute GitCandy executable path without line breaks.")
            .ValidateOnStart();

        return services;
    }

    private static IServiceCollection AddGitCandyProxyOptions(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(GitCandyProxyOptions.SectionName);
        var settings = section.Get<GitCandyProxyOptions>() ?? new GitCandyProxyOptions();
        services.AddOptions<GitCandyProxyOptions>().Bind(section)
            .Validate(options => !options.Enabled || (options.ForwardLimit is >= 1 and <= 10 && options.KnownProxies.Length > 0
                && options.KnownProxies.All(static value => IPAddress.TryParse(value, out _))),
                "Enabled proxy forwarding requires a forward limit and explicit IP addresses.")
            .ValidateOnStart();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = settings.ForwardLimit;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var value in settings.KnownProxies) options.KnownProxies.Add(IPAddress.Parse(value));
        });
        return services;
    }

    private static IServiceCollection AddGitCandyNamespaceOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<GitCandyNamespaceOptions>()
            .Bind(configuration.GetSection(GitCandyNamespaceOptions.SectionName))
            .Validate(options => options.AliasRetentionDays is >= 1 and <= 3650,
                "AliasRetentionDays must be between 1 and 3650.")
            .Validate(options => options.RenameLimit is >= 1 and <= 20,
                "RenameLimit must be between 1 and 20.")
            .Validate(options => options.RenameWindowDays is >= 1 and <= 365,
                "RenameWindowDays must be between 1 and 365.")
            .ValidateOnStart();
        return services;
    }

    private static GitCandyIdentityOptions AddGitCandyIdentityOptions(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(GitCandyIdentityOptions.SectionName);
        var settings = section.Get<GitCandyIdentityOptions>() ?? new GitCandyIdentityOptions();

        services.AddOptions<GitCandyIdentityOptions>()
            .Bind(section)
            .Validate(
                options => options.Password.RequiredLength is >= 8 and <= 128,
                $"{nameof(GitCandyPasswordOptions.RequiredLength)} must be between 8 and 128.")
            .Validate(
                options => options.Password.RequiredUniqueChars is >= 1 and <= 128
                    && options.Password.RequiredUniqueChars <= options.Password.RequiredLength,
                $"{nameof(GitCandyPasswordOptions.RequiredUniqueChars)} must be between 1 and the required password length.")
            .Validate(
                options => options.AccountRecovery.TokenLifespan > TimeSpan.Zero
                    && options.AccountRecovery.TokenLifespan <= TimeSpan.FromDays(1),
                "Account recovery token lifespan must be between zero and one day.")
            .Validate(
                options => options.AccountRecovery.MaxRequestsPerWindow is >= 1 and <= 100
                    && options.AccountRecovery.RequestWindow > TimeSpan.Zero,
                "Account recovery rate-limit settings are invalid.")
            .Validate(
                options => string.IsNullOrWhiteSpace(options.AccountRecovery.Smtp.Host)
                    || (!string.IsNullOrWhiteSpace(options.AccountRecovery.Smtp.FromAddress)
                        && options.AccountRecovery.Smtp.Port is >= 1 and <= 65535),
                "Enabled SMTP delivery requires a from address and valid port.")
            .Validate(
                options => !options.OpenIdConnect.Enabled
                    || (!string.IsNullOrWhiteSpace(options.OpenIdConnect.DisplayName)
                        && Uri.TryCreate(options.OpenIdConnect.Authority, UriKind.Absolute, out var authority)
                        && (!options.OpenIdConnect.RequireHttpsMetadata
                            || string.Equals(authority.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                        && !string.IsNullOrWhiteSpace(options.OpenIdConnect.ClientId)
                        && !string.IsNullOrWhiteSpace(options.OpenIdConnect.ClientSecret)
                        && options.OpenIdConnect.CallbackPath.StartsWith("/", StringComparison.Ordinal)),
                "Enabled OpenID Connect login requires a display name, absolute authority, client id, client secret, and root-relative callback path.")
            .ValidateOnStart();

        return settings;
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

    private static IServiceCollection AddRepositoryBrowserOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RepositoryBrowserOptions>()
            .Bind(configuration.GetSection(RepositoryBrowserOptions.SectionName))
            .Validate(options => options.MaxDisplayedBlobBytes > 0, "MaxDisplayedBlobBytes must be greater than zero.")
            .Validate(options => options.MaxDiffCharacters > 0, "MaxDiffCharacters must be greater than zero.")
            .Validate(options => options.MaxDiffFiles > 0, "MaxDiffFiles must be greater than zero.")
            .Validate(options => options.MaxArchiveBytes > 0, "MaxArchiveBytes must be greater than zero.")
            .Validate(options => options.MaxArchiveEntries > 0, "MaxArchiveEntries must be greater than zero.")
            .Validate(options => options.OperationTimeout > TimeSpan.Zero, "OperationTimeout must be greater than zero.")
            .Validate(options => options.MaxStatisticsCommits is >= 1 and <= 1_000_000, "MaxStatisticsCommits is invalid.")
            .Validate(options => options.MaxContributors is >= 1 and <= 10_000, "MaxContributors is invalid.")
            .ValidateOnStart();
        var browserSettings = configuration
            .GetSection(RepositoryBrowserOptions.SectionName)
            .Get<RepositoryBrowserOptions>() ?? new RepositoryBrowserOptions();
        services.AddRequestTimeouts(options => options.AddPolicy(
            RepositoryBrowserOptions.RequestTimeoutPolicyName,
            browserSettings.OperationTimeout));
        services.AddOptions<GitLfsOptions>()
            .Bind(configuration.GetSection(GitLfsOptions.SectionName))
            .Validate(options => options.MaxObjectBytes > 0, "MaxObjectBytes must be greater than zero.")
            .Validate(options => options.RepositoryQuotaBytes >= 0, "RepositoryQuotaBytes cannot be negative.")
            .Validate(options => options.StreamBufferSize >= 4096, "StreamBufferSize must be at least 4096 bytes.")
            .Validate(options => options.OperationTimeout > TimeSpan.Zero, "OperationTimeout must be greater than zero.")
            .ValidateOnStart();

        return services;
    }
}
