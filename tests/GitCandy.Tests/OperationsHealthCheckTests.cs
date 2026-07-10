using GitCandy.Configuration;
using GitCandy.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GitCandy.Tests;

[TestClass]
public sealed class OperationsHealthCheckTests
{
    [TestMethod]
    public async Task MigrateGitCandyDatabaseAsync_WithEmptyDataDirectory_CreatesReadyApplication()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var repositoryPath = Path.Combine(tempRoot, "repositories");
            var cachePath = Path.Combine(tempRoot, "cache");
            var databasePath = Path.Combine(tempRoot, "GitCandy.db");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Application:RepositoryPath"] = repositoryPath,
                    ["GitCandy:Application:CachePath"] = cachePath,
                    ["GitCandy:Application:EnableSsh"] = "false",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath}"
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment(tempRoot));
            services.AddGitCandyWebShell(configuration);

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await serviceProvider.MigrateGitCandyDatabaseAsync();

            var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();
            var result = await healthCheckService.CheckHealthAsync(
                registration => registration.Tags.Contains("ready"));

            Assert.AreEqual(HealthStatus.Healthy, result.Status);
            Assert.IsTrue(File.Exists(databasePath));
            Assert.IsTrue(Directory.Exists(repositoryPath));
            Assert.IsTrue(Directory.Exists(cachePath));
            Assert.HasCount(5, result.Entries);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gitcandy-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
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
}
