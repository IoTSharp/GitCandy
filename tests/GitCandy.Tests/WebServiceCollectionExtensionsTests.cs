using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Caching;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Diagnostics;
using GitCandy.Git;
using GitCandy.Profiling;
using GitCandy.Schedules;
using GitCandy.Ssh;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quartz;
using Quartz.Logging;

namespace GitCandy.Tests;

[TestClass]
public sealed class WebServiceCollectionExtensionsTests
{
    [TestMethod]
    public async Task AddGitCandyWebShell_WithSqliteProvider_RegistersAuthenticationAuthorizationAndLocalization()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(tempRoot, "GitCandy.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment(tempRoot));
            services.AddGitCandyWebShell(configuration);

            Assert.IsTrue(services.Any(service =>
                service.ServiceType == typeof(IHostedService)
                && service.ImplementationType == typeof(GitCandyHostDiagnosticsHostedService)));
            Assert.IsTrue(services.Any(service =>
                service.ServiceType == typeof(IHostedService)
                && service.ImplementationType == typeof(SshServerHostedService)));

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var schemeProvider = serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
            var identityScheme = await schemeProvider.GetSchemeAsync(IdentityConstants.ApplicationScheme);
            Assert.IsNotNull(identityScheme);
            var gitBasicScheme = await schemeProvider.GetSchemeAsync(GitCandyAuthenticationSchemes.GitBasic);
            Assert.IsNotNull(gitBasicScheme);

            var authorizationOptions = serviceProvider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
            Assert.IsNotNull(authorizationOptions.GetPolicy(AuthorizationPolicies.Administrator));
            Assert.IsNotNull(authorizationOptions.GetPolicy(AuthorizationPolicies.RepositoryRead));
            Assert.IsNotNull(authorizationOptions.GetPolicy(AuthorizationPolicies.RepositoryWrite));
            Assert.IsNotNull(authorizationOptions.GetPolicy(AuthorizationPolicies.RepositoryOwner));
            Assert.IsNotNull(authorizationOptions.GetPolicy(AuthorizationPolicies.TeamAdministrator));
            Assert.IsNotNull(authorizationOptions.GetPolicy(AuthorizationPolicies.CurrentUser));

            var cookieOptions = serviceProvider
                .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(IdentityConstants.ApplicationScheme);
            Assert.AreEqual(".GitCandy.Identity", cookieOptions.Cookie.Name);
            Assert.IsTrue(cookieOptions.Cookie.HttpOnly);
            Assert.AreEqual(SameSiteMode.Lax, cookieOptions.Cookie.SameSite);
            Assert.AreEqual(CookieSecurePolicy.Always, cookieOptions.Cookie.SecurePolicy);
            Assert.AreEqual(TimeSpan.FromHours(8), cookieOptions.ExpireTimeSpan);
            Assert.IsTrue(cookieOptions.SlidingExpiration);
            Assert.IsFalse(services.Any(service =>
                string.Equals(
                    service.ServiceType.FullName,
                    "Microsoft.AspNetCore.Session.ISessionStore",
                    StringComparison.Ordinal)));

            Assert.IsNotNull(serviceProvider.GetRequiredService<IMemoryCache>());
            Assert.IsNotNull(serviceProvider.GetRequiredService<IApplicationCache>());
            Assert.IsNotNull(serviceProvider.GetRequiredService<IHttpContextAccessor>());
            Assert.IsNotNull(serviceProvider.GetRequiredService<IRequestProfilerAccessor>());
            Assert.IsInstanceOfType<BuiltInSshServerRuntime>(
                serviceProvider.GetRequiredService<ISshServerRuntime>());

            var localizationOptions = serviceProvider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
            Assert.AreEqual("en-US", localizationOptions.DefaultRequestCulture.Culture.Name);
            Assert.IsTrue(localizationOptions.SupportedCultures?.Any(culture => culture.Name == "zh-Hans") == true);

            var applicationOptions = serviceProvider.GetRequiredService<IOptions<GitCandyApplicationOptions>>().Value;
            Assert.AreEqual("App_Data/config.xml", applicationOptions.UserConfigurationPath);
            Assert.AreEqual("App_Data/Repos", applicationOptions.RepositoryPath);
            Assert.AreEqual("App_Data/Caches", applicationOptions.CachePath);
            Assert.AreEqual(30, applicationOptions.NumberOfCommitsPerPage);
            Assert.AreEqual(30, applicationOptions.NumberOfItemsPerList);
            Assert.AreEqual(50, applicationOptions.NumberOfRepositoryContributors);
            Assert.AreEqual(22, applicationOptions.SshPort);
            Assert.IsTrue(applicationOptions.IsPublicServer);
            Assert.IsTrue(applicationOptions.AllowRegisterUser);
            Assert.IsTrue(applicationOptions.AllowRepositoryCreation);
            Assert.IsTrue(applicationOptions.EnableSsh);
            Assert.AreEqual("App_Data/ssh-host-key.xml", applicationOptions.SshHostKeyPath);

            await using var scope = serviceProvider.CreateAsyncScope();
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<GitCandyDbContext>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IMembershipService>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IRepositoryService>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<ICurrentUser>());
            var authorizationHandlers = scope.ServiceProvider.GetServices<IAuthorizationHandler>().ToArray();
            Assert.IsTrue(authorizationHandlers.Any(handler => handler is RepositoryAuthorizationHandler));
            Assert.IsTrue(authorizationHandlers.Any(handler => handler is TeamAdministratorAuthorizationHandler));
            Assert.IsTrue(authorizationHandlers.Any(handler => handler is CurrentUserAuthorizationHandler));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task AddGitCandyWebShell_WithMefReplacementServices_ResolvesApplicationGitAndSchedulerServices()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(tempRoot, "GitCandy.db");

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = GetWebProjectRoot(),
                EnvironmentName = Environments.Development
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "sqlite",
                ["GitCandy:Application:EnableSsh"] = "false",
                ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
            });
            builder.Services.AddGitCandyWebShell(builder.Configuration);

            await using var app = builder.Build();
            await using var scope = app.Services.CreateAsyncScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.EnsureCreatedAsync();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var adminRoleResult = await roleManager.CreateAsync(new IdentityRole(RoleNames.Administrator));
            Assert.IsTrue(adminRoleResult.Succeeded);

            var user = new GitCandyUser
            {
                Id = "user-admin",
                UserName = "admin",
                Email = "admin@gitcandy.local"
            };
            var createUserResult = await userManager.CreateAsync(user);
            Assert.IsTrue(createUserResult.Succeeded);

            var addRoleResult = await userManager.AddToRoleAsync(user, RoleNames.Administrator);
            Assert.IsTrue(addRoleResult.Succeeded);

            var now = DateTime.UtcNow;
            dbContext.Repositories.AddRange(
                new GitCandyRepository
                {
                    Name = "public-demo",
                    Description = "Public repository",
                    CreatedAtUtc = now,
                    AllowAnonymousRead = true
                },
                new GitCandyRepository
                {
                    Name = "private-demo",
                    Description = "Private repository",
                    CreatedAtUtc = now,
                    IsPrivate = true
                });
            await dbContext.SaveChangesAsync();

            var membershipService = scope.ServiceProvider.GetRequiredService<IMembershipService>();
            var foundByEmail = await membershipService.FindUserAsync("admin@gitcandy.local");
            Assert.IsNotNull(foundByEmail);
            Assert.IsTrue(await membershipService.IsAdministratorAsync(foundByEmail.Id));

            var repositoryService = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            var repository = await repositoryService.FindRepositoryAsync("PUBLIC-DEMO");
            Assert.IsNotNull(repository);
            Assert.AreEqual("public-demo", repository.Name);
            Assert.IsTrue(await repositoryService.CanReadRepositoryAsync("public-demo", null, isAdministrator: false));
            Assert.IsFalse(await repositoryService.CanReadRepositoryAsync("private-demo", null, isAdministrator: false));

            var visibleRepositories = await repositoryService.GetVisibleRepositoriesAsync(null, isAdministrator: false);
            Assert.AreEqual(1, visibleRepositories.Count);
            Assert.AreEqual("public-demo", visibleRepositories[0].Name);

            var gitFactory = scope.ServiceProvider.GetRequiredService<IGitServiceFactory>();
            var gitContext = gitFactory.Create("public-demo");
            Assert.AreEqual("public-demo", gitContext.RepositoryName);
            StringAssert.EndsWith(
                gitContext.RepositoryPath,
                Path.Combine("App_Data", "Repos", "public-demo"));
            Assert.ThrowsExactly<ArgumentException>(() => gitFactory.Create(@"..\escape"));
            Assert.ThrowsExactly<ArgumentException>(() => gitFactory.Create("../escape"));
            Assert.ThrowsExactly<ArgumentException>(() => gitFactory.Create("nested/repository"));
            Assert.ThrowsExactly<ArgumentException>(() => gitFactory.Create("."));
            Assert.ThrowsExactly<ArgumentException>(() => gitFactory.Create(".."));

        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task AddGitCandyWebShell_WithQuartzScheduler_StartsAndExecutesRegisteredSchedulerJob()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(tempRoot, "GitCandy.db");
        var completion = new TaskCompletionSource<SchedulerJobContext>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var webProjectRoot = GetWebProjectRoot();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = webProjectRoot,
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseKestrel();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "sqlite",
                ["GitCandy:Application:EnableSsh"] = "false",
                ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
            });
            builder.Services.AddSingleton(completion);
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            builder.Services.AddScoped<ISchedulerJob, RecordingSchedulerJob>();

            await using var app = builder.Build();
            ResetQuartzLogging();
            await app.StartAsync();

            try
            {
                var completedTask = await Task.WhenAny(
                    completion.Task,
                    Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.AreSame(completion.Task, completedTask);

                var schedulerFactory = app.Services.GetRequiredService<ISchedulerFactory>();
                var scheduler = await schedulerFactory.GetScheduler();
                Assert.IsTrue(scheduler.IsStarted);

                var jobDetail = await scheduler.GetJobDetail(
                    new JobKey(RecordingSchedulerJob.JobName, "GitCandy.Scheduler"));
                Assert.IsNotNull(jobDetail);

                var schedulerContext = await completion.Task;
                Assert.AreEqual(1, schedulerContext.ExecutionTimes);
                Assert.IsNull(schedulerContext.UtcLastStart);
                Assert.IsNull(schedulerContext.UtcLastEnd);
            }
            finally
            {
                await app.StopAsync();
                ResetQuartzLogging();
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task AddGitCandyWebShell_WithRunningQuartzJob_CancelsAndWaitsDuringHostShutdown()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            Guid.NewGuid().ToString("N"));
        var signals = new SchedulerShutdownSignals();

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = GetWebProjectRoot(),
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseKestrel();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "sqlite",
                ["GitCandy:Application:EnableSsh"] = "false",
                ["ConnectionStrings:GitCandy"] =
                    $"Data Source={Path.Combine(tempRoot, "GitCandy.db")};Pooling=False"
            });
            builder.Services.AddSingleton(signals);
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            builder.Services.AddScoped<ISchedulerJob, BlockingSchedulerJob>();

            await using var app = builder.Build();
            ResetQuartzLogging();
            await app.StartAsync();

            var startedTask = await Task.WhenAny(
                signals.Started.Task,
                Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.AreSame(signals.Started.Task, startedTask);

            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsTrue(await signals.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            ResetQuartzLogging();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task SshServerHostedService_WithEnabledSsh_StartsAndStopsRuntimeWithConfiguredPort()
    {
        var runtime = new RecordingSshServerRuntime();
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["GitCandy:Application:EnableSsh"] = "true",
                ["GitCandy:Application:SshPort"] = "2022"
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISshServerRuntime>(runtime);
        services.AddGitCandyWebShell(configuration);

        await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var hostedService = ActivatorUtilities.CreateInstance<SshServerHostedService>(serviceProvider);

        await hostedService.StartAsync(CancellationToken.None);

        using var stopTokenSource = new CancellationTokenSource();
        await hostedService.StopAsync(stopTokenSource.Token);

        Assert.AreEqual(1, runtime.StartCalls);
        Assert.AreEqual(2022, runtime.StartedPort);
        Assert.AreEqual(1, runtime.StopCalls);
        Assert.IsTrue(runtime.StopCancellationCanBeCanceled);
    }

    [TestMethod]
    public async Task SshServerHostedService_WithDisabledSsh_DoesNotStartRuntime()
    {
        var runtime = new RecordingSshServerRuntime();
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["GitCandy:Application:EnableSsh"] = "false",
                ["GitCandy:Application:SshPort"] = "2022"
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISshServerRuntime>(runtime);
        services.AddGitCandyWebShell(configuration);

        await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var hostedService = ActivatorUtilities.CreateInstance<SshServerHostedService>(serviceProvider);

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StopAsync(CancellationToken.None);

        Assert.AreEqual(0, runtime.StartCalls);
        Assert.AreEqual(0, runtime.StopCalls);
    }

    [TestMethod]
    public void AddGitCandyWebShell_WithApplicationConfiguration_BindsStronglyTypedOptions()
    {
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["GitCandy:Application:UserConfigurationPath"] = "conf/gitcandy.xml",
                ["GitCandy:Application:IsPublicServer"] = "false",
                ["GitCandy:Application:ForceSsl"] = "true",
                ["GitCandy:Application:SslPort"] = "8443",
                ["GitCandy:Application:LocalSkipCustomError"] = "false",
                ["GitCandy:Application:AllowRegisterUser"] = "false",
                ["GitCandy:Application:AllowRepositoryCreation"] = "false",
                ["GitCandy:Application:RepositoryPath"] = "data/repositories",
                ["GitCandy:Application:CachePath"] = "data/cache",
                ["GitCandy:Application:GitCorePath"] = "C:\\Git\\mingw64\\libexec\\git-core",
                ["GitCandy:Application:NumberOfCommitsPerPage"] = "25",
                ["GitCandy:Application:NumberOfItemsPerList"] = "15",
                ["GitCandy:Application:NumberOfRepositoryContributors"] = "9",
                ["GitCandy:Application:SshPort"] = "2022",
                ["GitCandy:Application:EnableSsh"] = "false",
                ["GitCandy:Application:SshHostKeyPath"] = "secrets/ssh-host-key.xml"
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGitCandyWebShell(configuration);

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var options = serviceProvider.GetRequiredService<IOptions<GitCandyApplicationOptions>>().Value;

        Assert.AreEqual("conf/gitcandy.xml", options.UserConfigurationPath);
        Assert.IsFalse(options.IsPublicServer);
        Assert.IsTrue(options.ForceSsl);
        Assert.AreEqual(8443, options.SslPort);
        Assert.IsFalse(options.LocalSkipCustomError);
        Assert.IsFalse(options.AllowRegisterUser);
        Assert.IsFalse(options.AllowRepositoryCreation);
        Assert.AreEqual("data/repositories", options.RepositoryPath);
        Assert.AreEqual("data/cache", options.CachePath);
        Assert.AreEqual("C:\\Git\\mingw64\\libexec\\git-core", options.GitCorePath);
        Assert.AreEqual(25, options.NumberOfCommitsPerPage);
        Assert.AreEqual(15, options.NumberOfItemsPerList);
        Assert.AreEqual(9, options.NumberOfRepositoryContributors);
        Assert.AreEqual(2022, options.SshPort);
        Assert.IsFalse(options.EnableSsh);
        Assert.AreEqual("secrets/ssh-host-key.xml", options.SshHostKeyPath);
    }

    [TestMethod]
    public void AddGitCandyWebShell_WithLegacyUserConfigurationSetting_UsesMigrationAlias()
    {
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                [GitCandyApplicationOptions.LegacyUserConfigurationKey] = "~\\App_Data\\custom.config.xml"
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGitCandyWebShell(configuration);

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var options = serviceProvider.GetRequiredService<IOptions<GitCandyApplicationOptions>>().Value;

        Assert.AreEqual("~\\App_Data\\custom.config.xml", options.UserConfigurationPath);
    }

    [TestMethod]
    public async Task AddGitCandyWebShell_WithInvalidApplicationConfiguration_FailsOnHostStart()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = GetWebProjectRoot(),
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GitCandy:Application:NumberOfItemsPerList"] = "0",
            ["GitCandy:Database:Provider"] = "sqlite",
            ["ConnectionStrings:GitCandy"] = "Data Source=:memory:"
        });
        builder.Services.AddGitCandyWebShell(builder.Configuration);

        await using var app = builder.Build();

        ResetQuartzLogging();
        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => app.StartAsync());
    }

    [TestMethod]
    public async Task AddGitCandyWebShell_WithEscapingRelativeApplicationPath_FailsOnHostStart()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = GetWebProjectRoot(),
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GitCandy:Application:RepositoryPath"] = "../Repos",
            ["GitCandy:Database:Provider"] = "sqlite",
            ["ConnectionStrings:GitCandy"] = "Data Source=:memory:"
        });
        builder.Services.AddGitCandyWebShell(builder.Configuration);

        await using var app = builder.Build();

        ResetQuartzLogging();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => app.StartAsync());
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        var configurationValues = new Dictionary<string, string?>(values)
        {
            ["GitCandy:Database:Provider"] = "sqlite",
            ["ConnectionStrings:GitCandy"] = "Data Source=:memory:"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
    }

    private static string GetWebProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GitCandy.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory);
        return Path.Combine(directory.FullName, "src", "GitCandy");
    }

    private static void ResetQuartzLogging()
    {
        LogProvider.SetCurrentLogProvider(new NoopQuartzLogProvider());
    }

    private sealed class RecordingSchedulerJob(
        TaskCompletionSource<SchedulerJobContext> completion) : ISchedulerJob
    {
        public const string JobName = "RecordingSchedulerJob";

        private readonly TaskCompletionSource<SchedulerJobContext> _completion = completion;

        public string Name => JobName;

        public SchedulerJobType JobType => SchedulerJobType.RealTime;

        public ValueTask ExecuteAsync(
            SchedulerJobContext context,
            CancellationToken cancellationToken = default)
        {
            _completion.TrySetResult(context);
            return ValueTask.CompletedTask;
        }

        public TimeSpan GetNextInterval(SchedulerJobContext context)
        {
            return TimeSpan.MaxValue;
        }
    }

    private sealed class BlockingSchedulerJob(SchedulerShutdownSignals signals) : ISchedulerJob
    {
        private readonly SchedulerShutdownSignals _signals = signals;

        public string Name => "BlockingSchedulerJob";

        public SchedulerJobType JobType => SchedulerJobType.RealTime;

        public async ValueTask ExecuteAsync(
            SchedulerJobContext context,
            CancellationToken cancellationToken = default)
        {
            _signals.Started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                _signals.Canceled.TrySetResult(cancellationToken.IsCancellationRequested);
            }
        }

        public TimeSpan GetNextInterval(SchedulerJobContext context)
        {
            return TimeSpan.MaxValue;
        }
    }

    private sealed class SchedulerShutdownSignals
    {
        public TaskCompletionSource<bool> Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Canceled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RecordingSshServerRuntime : ISshServerRuntime
    {
        public bool IsRunning { get; private set; }

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public int StartedPort { get; private set; }

        public bool StopCancellationCanBeCanceled { get; private set; }

        public Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            StartCalls++;
            StartedPort = port;
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            StopCancellationCanBeCanceled = cancellationToken.CanBeCanceled;
            IsRunning = false;
            return Task.CompletedTask;
        }
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GitCandy.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = contentRootPath;

        public string EnvironmentName { get; set; } = Environments.Development;

        public string WebRootPath { get; set; } = contentRootPath;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class NoopQuartzLogProvider : ILogProvider
    {
        public Logger GetLogger(string name)
        {
            return (_, _, _, _) => false;
        }

        public IDisposable OpenNestedContext(string message)
        {
            return NullScope.Instance;
        }

        public IDisposable OpenMappedContext(string key, object value, bool destructure)
        {
            return NullScope.Instance;
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
