using GitCandy.Application;
using GitCandy.Configuration;
using GitCandy.Caching;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Git;
using GitCandy.Schedules;
using Microsoft.AspNetCore.Authentication;
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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class WebServiceCollectionExtensionsTests
{
    [TestMethod]
    public async Task AddGitCandyWebShell_WithSqliteProvider_RegistersAuthSessionAndLocalizationPlaceholders()
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
            services.AddGitCandyWebShell(configuration);

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var schemeProvider = serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
            var identityScheme = await schemeProvider.GetSchemeAsync(IdentityConstants.ApplicationScheme);
            Assert.IsNotNull(identityScheme);

            var authorizationOptions = serviceProvider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
            Assert.IsNotNull(authorizationOptions.GetPolicy(AuthorizationPolicies.Administrator));

            var sessionOptions = serviceProvider.GetRequiredService<IOptions<SessionOptions>>().Value;
            Assert.AreEqual(".GitCandy.Session", sessionOptions.Cookie.Name);

            Assert.IsNotNull(serviceProvider.GetRequiredService<IMemoryCache>());
            Assert.IsNotNull(serviceProvider.GetRequiredService<IApplicationCache>());

            var localizationOptions = serviceProvider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
            Assert.AreEqual("en-US", localizationOptions.DefaultRequestCulture.Culture.Name);
            Assert.IsTrue(localizationOptions.SupportedCultures?.Any(culture => culture.Name == "zh-Hans") == true);

            var applicationOptions = serviceProvider.GetRequiredService<IOptions<GitCandyApplicationOptions>>().Value;
            Assert.AreEqual("App_Data/{0}.log", applicationOptions.LogPathFormat);
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

            await using var scope = serviceProvider.CreateAsyncScope();
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<GitCandyDbContext>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IMembershipService>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IRepositoryService>());
            Assert.IsTrue(scope.ServiceProvider.GetServices<ISchedulerJob>().Any(job => job is LogRotationJob));
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

            var jobs = scope.ServiceProvider.GetServices<ISchedulerJob>().ToArray();
            Assert.AreEqual(1, jobs.Length);
            Assert.AreEqual("LegacyLogRotation", jobs[0].Name);
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
    public void AddGitCandyWebShell_WithApplicationConfiguration_BindsStronglyTypedOptions()
    {
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["GitCandy:Application:LogPathFormat"] = "logs/{0}.txt",
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
                ["GitCandy:Application:EnableSsh"] = "false"
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGitCandyWebShell(configuration);

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var options = serviceProvider.GetRequiredService<IOptions<GitCandyApplicationOptions>>().Value;

        Assert.AreEqual("logs/{0}.txt", options.LogPathFormat);
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
    }

    [TestMethod]
    public void AddGitCandyWebShell_WithLegacyAppSettings_UsesMigrationAliases()
    {
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                [GitCandyApplicationOptions.LegacyLogPathFormatKey] = "~\\App_Data\\{0}.log",
                [GitCandyApplicationOptions.LegacyUserConfigurationKey] = "~\\App_Data\\custom.config.xml"
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGitCandyWebShell(configuration);

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var options = serviceProvider.GetRequiredService<IOptions<GitCandyApplicationOptions>>().Value;

        Assert.AreEqual("~\\App_Data\\{0}.log", options.LogPathFormat);
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

        await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => app.StartAsync());
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
}
