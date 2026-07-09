using GitCandy.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class GitCandyApplicationPathsTests
{
    [TestMethod]
    public void GitCandyApplicationPaths_WithDefaultRelativeOptions_ResolvesFromContentRoot()
    {
        var contentRoot = CreateTestPath("content");
        var webRoot = Path.Combine(contentRoot, "wwwroot");

        var paths = new GitCandyApplicationPaths(
            CreateEnvironment(contentRoot, webRoot),
            Options.Create(new GitCandyApplicationOptions()));

        Assert.AreEqual(FullPath(contentRoot), paths.ContentRootPath);
        Assert.AreEqual(FullPath(webRoot), paths.WebRootPath);
        Assert.AreEqual(FullPath(contentRoot, "App_Data", "{0}.log"), paths.LogPathFormat);
        Assert.AreEqual(FullPath(contentRoot, "App_Data", "config.xml"), paths.UserConfigurationPath);
        Assert.AreEqual(FullPath(contentRoot, "App_Data", "Repos"), paths.RepositoryPath);
        Assert.AreEqual(FullPath(contentRoot, "App_Data", "Caches"), paths.CachePath);
        Assert.AreEqual(string.Empty, paths.GitCorePath);
    }

    [TestMethod]
    public void GitCandyApplicationPaths_WithLegacyVirtualPaths_ResolvesFromContentRoot()
    {
        var contentRoot = CreateTestPath("content");
        var webRoot = Path.Combine(contentRoot, "wwwroot");
        var options = new GitCandyApplicationOptions
        {
            LogPathFormat = @"~\App_Data\{0}.log",
            UserConfigurationPath = "~/App_Data/custom.config.xml",
            RepositoryPath = @"~\Repositories",
            CachePath = "~/Caches",
            GitCorePath = @"~\git-core"
        };

        var paths = new GitCandyApplicationPaths(
            CreateEnvironment(contentRoot, webRoot),
            Options.Create(options));

        Assert.AreEqual(FullPath(contentRoot, "App_Data", "{0}.log"), paths.LogPathFormat);
        Assert.AreEqual(FullPath(contentRoot, "App_Data", "custom.config.xml"), paths.UserConfigurationPath);
        Assert.AreEqual(FullPath(contentRoot, "Repositories"), paths.RepositoryPath);
        Assert.AreEqual(FullPath(contentRoot, "Caches"), paths.CachePath);
        Assert.AreEqual(FullPath(contentRoot, "git-core"), paths.GitCorePath);
    }

    [TestMethod]
    public void GitCandyApplicationPaths_WithAbsoluteOptions_KeepsAbsolutePaths()
    {
        var contentRoot = CreateTestPath("content");
        var externalRoot = CreateTestPath("external");
        var options = new GitCandyApplicationOptions
        {
            LogPathFormat = Path.Combine(externalRoot, "logs", "{0}.log"),
            UserConfigurationPath = Path.Combine(externalRoot, "config", "gitcandy.xml"),
            RepositoryPath = Path.Combine(externalRoot, "repositories"),
            CachePath = Path.Combine(externalRoot, "cache"),
            GitCorePath = Path.Combine(externalRoot, "git-core")
        };

        var paths = new GitCandyApplicationPaths(
            CreateEnvironment(contentRoot, Path.Combine(contentRoot, "wwwroot")),
            Options.Create(options));

        Assert.AreEqual(FullPath(externalRoot, "logs", "{0}.log"), paths.LogPathFormat);
        Assert.AreEqual(FullPath(externalRoot, "config", "gitcandy.xml"), paths.UserConfigurationPath);
        Assert.AreEqual(FullPath(externalRoot, "repositories"), paths.RepositoryPath);
        Assert.AreEqual(FullPath(externalRoot, "cache"), paths.CachePath);
        Assert.AreEqual(FullPath(externalRoot, "git-core"), paths.GitCorePath);
    }

    [TestMethod]
    public void GitCandyApplicationPaths_WithWebRoot_ResolvesWebRootPaths()
    {
        var contentRoot = CreateTestPath("content");
        var webRoot = Path.Combine(contentRoot, "wwwroot");
        var paths = new GitCandyApplicationPaths(
            CreateEnvironment(contentRoot, webRoot),
            Options.Create(new GitCandyApplicationOptions()));

        Assert.AreEqual(FullPath(webRoot, "css", "site.css"), paths.ResolveWebRootPath("~/css/site.css"));
    }

    [TestMethod]
    public void GitCandyApplicationPaths_WithoutWebRoot_ThrowsForWebRootPath()
    {
        var paths = new GitCandyApplicationPaths(
            CreateEnvironment(CreateTestPath("content"), webRootPath: string.Empty),
            Options.Create(new GitCandyApplicationOptions()));

        Assert.ThrowsExactly<InvalidOperationException>(() => paths.ResolveWebRootPath("~/css/site.css"));
    }

    [TestMethod]
    public void AddGitCandyWebShell_WithWebHostEnvironment_RegistersApplicationPaths()
    {
        var contentRoot = CreateTestPath("content");
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["GitCandy:Application:RepositoryPath"] = "data/repos",
                ["GitCandy:Application:CachePath"] = "data/cache"
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IWebHostEnvironment>(CreateEnvironment(contentRoot, Path.Combine(contentRoot, "wwwroot")));
        services.AddGitCandyWebShell(configuration);

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var paths = serviceProvider.GetRequiredService<IGitCandyApplicationPaths>();

        Assert.AreEqual(FullPath(contentRoot, "data", "repos"), paths.RepositoryPath);
        Assert.AreEqual(FullPath(contentRoot, "data", "cache"), paths.CachePath);
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

    private static IWebHostEnvironment CreateEnvironment(string contentRootPath, string webRootPath)
    {
        return new TestWebHostEnvironment
        {
            ContentRootPath = contentRootPath,
            WebRootPath = webRootPath
        };
    }

    private static string CreateTestPath(string segment)
    {
        return Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            "Paths",
            Guid.NewGuid().ToString("N"),
            segment);
    }

    private static string FullPath(params string[] paths)
    {
        return Path.GetFullPath(Path.Combine(paths));
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GitCandy.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = Environments.Development;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;
    }
}
