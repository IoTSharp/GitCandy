using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GitCandy.Configuration;
using GitCandy.Controllers;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GitCandy.Tests;

[TestClass]
public sealed class GitSmartHttpIntegrationTests
{
    private const int LargeFileSize = 24 * 1024 * 1024;

    [TestMethod]
    public async Task GitSmartHttp_WithRealGitClient_ClonesFetchesAndPushesLargePack()
    {
        await using var fixture = await GitHttpFixture.CreateAsync();
        var clonePath = Path.Combine(fixture.TempRoot, "clones", "public-dotgit");
        var noSuffixClonePath = Path.Combine(fixture.TempRoot, "clones", "public-no-suffix");

        await fixture.RunGitAsync(
            ["-c", "protocol.version=2", "clone", $"{fixture.BaseAddress}git/public-demo.git", clonePath]);
        Assert.IsTrue(File.Exists(Path.Combine(clonePath, "README.md")));

        await fixture.RunGitAsync(
            ["-c", "protocol.version=2", "clone", $"{fixture.BaseAddress}git/public-demo", noSuffixClonePath]);
        Assert.IsTrue(File.Exists(Path.Combine(noSuffixClonePath, "README.md")));

        File.AppendAllText(
            Path.Combine(fixture.SeedWorkTree, "README.md"),
            "fetch verification\n",
            Encoding.UTF8);
        await fixture.RunGitAsync(["-C", fixture.SeedWorkTree, "add", "README.md"]);
        await fixture.RunGitAsync(["-C", fixture.SeedWorkTree, "commit", "-m", "M6 fetch verification"]);
        await fixture.RunGitAsync(["-C", fixture.SeedWorkTree, "push", "origin", "main"]);
        await fixture.RunGitAsync(["-C", clonePath, "fetch", "--all", "--tags"]);

        var remoteHead = await fixture.RunGitForOutputAsync(
            ["-C", clonePath, "rev-parse", "origin/main"]);
        var bareHead = await fixture.RunGitForOutputAsync(
            ["--git-dir", fixture.BareRepositoryPath, "rev-parse", "refs/heads/main"]);
        Assert.AreEqual(bareHead, remoteHead);

        var largeFilePath = Path.Combine(clonePath, "large-pack.bin");
        await WriteRandomFileAsync(largeFilePath, LargeFileSize);
        await fixture.RunGitAsync(["-C", clonePath, "config", "user.name", "GitCandy M6 Bot"]);
        await fixture.RunGitAsync(["-C", clonePath, "config", "user.email", "m6@gitcandy.local"]);
        await fixture.RunGitAsync(["-C", clonePath, "add", "large-pack.bin"]);
        await fixture.RunGitAsync(["-C", clonePath, "commit", "-m", "M6 large pack streaming"]);
        await fixture.RunGitAsync(
            ["-C", clonePath, "push", "origin", "HEAD:refs/heads/m6-large-pack"],
            useOwnerCredentials: true);

        var remoteLargeFileSize = await fixture.RunGitForOutputAsync(
            [
                "--git-dir",
                fixture.BareRepositoryPath,
                "cat-file",
                "-s",
                "refs/heads/m6-large-pack:large-pack.bin"
            ]);
        Assert.AreEqual(
            LargeFileSize,
            int.Parse(remoteLargeFileSize, CultureInfo.InvariantCulture));
    }

    private static async Task WriteRandomFileAsync(string path, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            useAsync: true);
        var buffer = new byte[1024 * 1024];
        var remaining = size;
        while (remaining > 0)
        {
            RandomNumberGenerator.Fill(buffer);
            var count = Math.Min(buffer.Length, remaining);
            await stream.WriteAsync(buffer.AsMemory(0, count));
            remaining -= count;
        }
    }

    private sealed class GitHttpFixture : IAsyncDisposable
    {
        private const string OwnerUserName = "m6-owner";
        private const string OwnerPassword = "M6-owner-Password-1!";

        private GitHttpFixture(
            WebApplication app,
            string baseAddress,
            string bareRepositoryPath,
            string seedWorkTree,
            string tempRoot)
        {
            App = app;
            BaseAddress = baseAddress;
            BareRepositoryPath = bareRepositoryPath;
            SeedWorkTree = seedWorkTree;
            TempRoot = tempRoot;
        }

        private WebApplication App { get; }

        public string BaseAddress { get; }

        public string BareRepositoryPath { get; }

        public string SeedWorkTree { get; }

        public string TempRoot { get; }

        public static async Task<GitHttpFixture> CreateAsync()
        {
            var tempRoot = TestDirectory.Create();
            var repositoryRoot = Path.Combine(tempRoot, "Repositories");
            var bareRepositoryPath = Path.Combine(repositoryRoot, "public-demo.git");
            var seedWorkTree = Path.Combine(tempRoot, "seed-worktree");
            Directory.CreateDirectory(repositoryRoot);
            Directory.CreateDirectory(Path.Combine(tempRoot, "clones"));

            await RunGitProcessAsync(["init", "--bare", "--initial-branch=main", bareRepositoryPath]);
            await RunGitProcessAsync(["init", "--initial-branch=main", seedWorkTree]);
            File.WriteAllText(
                Path.Combine(seedWorkTree, "README.md"),
                "# GitCandy M6\n",
                Encoding.UTF8);
            await RunGitProcessAsync(["-C", seedWorkTree, "config", "user.name", "GitCandy M6 Bot"]);
            await RunGitProcessAsync(["-C", seedWorkTree, "config", "user.email", "m6@gitcandy.local"]);
            await RunGitProcessAsync(["-C", seedWorkTree, "add", "README.md"]);
            await RunGitProcessAsync(["-C", seedWorkTree, "commit", "-m", "M6 initial commit"]);
            await RunGitProcessAsync(["-C", seedWorkTree, "remote", "add", "origin", bareRepositoryPath]);
            await RunGitProcessAsync(["-C", seedWorkTree, "push", "origin", "main"]);

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
                ["ConnectionStrings:GitCandy"] = $"Data Source={Path.Combine(tempRoot, "GitCandy.db")};Pooling=False",
                ["GitCandy:Application:RepositoryPath"] = repositoryRoot,
                ["GitCandy:Application:CachePath"] = Path.Combine(tempRoot, "Caches"),
                ["GitCandy:Application:EnableSsh"] = "false",
                ["GitCandy:GitHttp:RequestTimeout"] = "00:05:00"
            });
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            builder.Services.AddControllersWithViews()
                .AddApplicationPart(typeof(GitController).Assembly);
            builder.Services.RemoveAll<IHostedService>();

            var app = builder.Build();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGitCandyCompatibilityRoutes();

            try
            {
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
                    await dbContext.Database.EnsureCreatedAsync();
                    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
                    var owner = new GitCandyUser
                    {
                        UserName = OwnerUserName,
                        Email = "m6-owner@example.com",
                        DisplayName = "M6 Owner"
                    };
                    var createResult = await userManager.CreateAsync(owner, OwnerPassword);
                    Assert.IsTrue(
                        createResult.Succeeded,
                        string.Join(", ", createResult.Errors.Select(error => error.Description)));

                    var repository = new GitCandyRepository
                    {
                        Name = "public-demo",
                        Description = "M6 real Git HTTP fixture",
                        CreatedAtUtc = DateTime.UtcNow,
                        AllowAnonymousRead = true
                    };
                    repository.UserRoles.Add(new GitCandyUserRepositoryRole
                    {
                        UserId = owner.Id,
                        AllowRead = true,
                        AllowWrite = true,
                        IsOwner = true
                    });
                    dbContext.Repositories.Add(repository);
                    await dbContext.SaveChangesAsync();
                }

                await app.StartAsync();
                var baseAddress = GetServerAddress(app);
                if (!baseAddress.EndsWith("/", StringComparison.Ordinal))
                {
                    baseAddress += "/";
                }

                return new GitHttpFixture(
                    app,
                    baseAddress,
                    bareRepositoryPath,
                    seedWorkTree,
                    tempRoot);
            }
            catch
            {
                await app.DisposeAsync();
                TestDirectory.Delete(tempRoot);
                throw;
            }
        }

        public Task RunGitAsync(
            IReadOnlyList<string> arguments,
            bool useOwnerCredentials = false)
        {
            return RunGitProcessAsync(arguments, useOwnerCredentials);
        }

        public async Task<string> RunGitForOutputAsync(IReadOnlyList<string> arguments)
        {
            var result = await RunGitProcessAsync(arguments);
            return result.StandardOutput.Trim();
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
            TestDirectory.Delete(TempRoot);
        }

        private static async Task<GitProcessResult> RunGitProcessAsync(
            IReadOnlyList<string> arguments,
            bool useOwnerCredentials = false)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "git.exe" : "git",
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GCM_INTERACTIVE"] = "Never";
            foreach (var traceVariable in new[]
            {
                "GIT_TRACE",
                "GIT_TRACE_CURL",
                "GIT_TRACE_PACKET",
                "GIT_CURL_VERBOSE"
            })
            {
                startInfo.Environment.Remove(traceVariable);
            }

            if (useOwnerCredentials)
            {
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{OwnerUserName}:{OwnerPassword}"));
                startInfo.Environment["GIT_CONFIG_COUNT"] = "1";
                startInfo.Environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
                startInfo.Environment["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {credentials}";
            }

            using var process = new Process { StartInfo = startInfo };
            Assert.IsTrue(process.Start());
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            Assert.AreEqual(
                0,
                process.ExitCode,
                $"git {arguments.FirstOrDefault()} failed: {standardError}");
            return new GitProcessResult(standardOutput, standardError);
        }
    }

    private sealed record GitProcessResult(string StandardOutput, string StandardError);

    private static string GetServerAddress(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?
            .Addresses;
        Assert.IsNotNull(addresses);
        return addresses.Single();
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
