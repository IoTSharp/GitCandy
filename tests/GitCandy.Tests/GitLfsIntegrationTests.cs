using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using GitCandy.Configuration;
using GitCandy.Controllers;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Git;
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
public sealed class GitLfsIntegrationTests
{
    private const int LfsObjectSize = 2 * 1024 * 1024;

    [TestMethod]
    public async Task GitLfs_WithRealClient_PushesFetchesClonesAndVerifiesSha256Object()
    {
        await using var fixture = await GitLfsFixture.CreateAsync();
        var workTree = Path.Combine(fixture.TempRoot, "lfs-source");
        var clonePath = Path.Combine(fixture.TempRoot, "lfs-clone");
        Directory.CreateDirectory(workTree);
        await fixture.RunGitAsync(["init", "--initial-branch=main", workTree]);
        await fixture.RunGitAsync(["-C", workTree, "config", "user.name", "GitCandy LFS"]);
        await fixture.RunGitAsync(["-C", workTree, "config", "user.email", "lfs@gitcandy.local"]);
        await fixture.RunGitAsync(["-C", workTree, "lfs", "install", "--local"]);
        await File.WriteAllTextAsync(Path.Combine(workTree, "README.md"), "# LFS integration\n");
        await fixture.RunGitAsync(["-C", workTree, "add", "README.md"]);
        await fixture.RunGitAsync(["-C", workTree, "commit", "-m", "Initial repository"]);
        await fixture.RunGitAsync(["-C", workTree, "lfs", "track", "*.bin"]);

        var objectPath = Path.Combine(workTree, "payload.bin");
        var content = RandomNumberGenerator.GetBytes(LfsObjectSize);
        await File.WriteAllBytesAsync(objectPath, content);
        var oid = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        await fixture.RunGitAsync(["-C", workTree, "add", ".gitattributes", "payload.bin"]);
        await fixture.RunGitAsync(["-C", workTree, "commit", "-m", "Add LFS payload"]);
        await fixture.RunGitAsync([
            "-C", workTree, "remote", "add", "origin", $"{fixture.BaseAddress}git/lfs-demo.git"]);
        await fixture.RunGitAsync(["-C", workTree, "push", "-u", "origin", "main"], authenticate: true);

        var headCommit = await fixture.RunGitForOutputAsync(["-C", workTree, "rev-parse", "HEAD"]);
        var baseCommit = await fixture.RunGitForOutputAsync(["-C", workTree, "rev-parse", "HEAD~1"]);
        using (var client = new HttpClient { BaseAddress = new Uri(fixture.BaseAddress) })
        {
            var treeHtml = await client.GetStringAsync("Repository/Tree/lfs-demo");
            StringAssert.Contains(treeHtml, "payload.bin");
            StringAssert.Contains(treeHtml, "Repository/Commits/lfs-demo");
            var blobHtml = await client.GetStringAsync("Repository/Blob/lfs-demo/.gitattributes");
            StringAssert.Contains(blobHtml, "id=\"L1\"");
            StringAssert.Contains(blobHtml, headCommit);
            var raw = await client.GetStringAsync("Repository/Raw/lfs-demo/.gitattributes");
            StringAssert.Contains(raw, "filter=lfs");
            var commitsHtml = await client.GetStringAsync("Repository/Commits/lfs-demo");
            StringAssert.Contains(commitsHtml, "Add LFS payload");
            var commitHtml = await client.GetStringAsync($"Repository/Commit/lfs-demo/{headCommit}");
            StringAssert.Contains(commitHtml, "payload.bin");
            var blameHtml = await client.GetStringAsync("Repository/Blame/lfs-demo/.gitattributes");
            StringAssert.Contains(blameHtml, "Blame");
            var compareHtml = await client.GetStringAsync(
                $"Repository/Compare/lfs-demo?baseRevision={baseCommit}&headRevision={headCommit}");
            StringAssert.Contains(compareHtml, "1</strong> ahead");
            using var archiveResponse = await client.GetAsync($"Repository/Archive/lfs-demo?revision={headCommit}");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, archiveResponse.StatusCode);
            Assert.AreEqual("application/zip", archiveResponse.Content.Headers.ContentType?.MediaType);
        }

        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IGitLfsObjectStore>();
            var info = store.GetInfo("lfs-demo", oid);
            Assert.IsNotNull(info);
            Assert.AreEqual(LfsObjectSize, info.Size);
        }

        using (var protocolClient = new HttpClient { BaseAddress = new Uri(fixture.BaseAddress) })
        {
            using var headRequest = new HttpRequestMessage(
                HttpMethod.Head,
                $"git/lfs-demo.git/info/lfs/objects/{oid}");
            using var headResponse = await protocolClient.SendAsync(headRequest);
            Assert.AreEqual(HttpStatusCode.OK, headResponse.StatusCode);
            Assert.AreEqual(LfsObjectSize, headResponse.Content.Headers.ContentLength);

            using var rangeRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"git/lfs-demo.git/info/lfs/objects/{oid}");
            rangeRequest.Headers.Range = new RangeHeaderValue(0, 31);
            using var rangeResponse = await protocolClient.SendAsync(rangeRequest);
            Assert.AreEqual(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
            Assert.AreEqual(32, (await rangeResponse.Content.ReadAsByteArrayAsync()).Length);

            using var privateBatch = new StringContent(
                $"{{\"operation\":\"download\",\"objects\":[{{\"oid\":\"{oid}\",\"size\":{LfsObjectSize}}}]}}",
                Encoding.UTF8,
                "application/vnd.git-lfs+json");
            using var privateResponse = await protocolClient.PostAsync(
                "git/private-lfs.git/info/lfs/objects/batch",
                privateBatch);
            Assert.AreEqual(HttpStatusCode.Unauthorized, privateResponse.StatusCode);
            Assert.IsTrue(privateResponse.Headers.WwwAuthenticate.Any(value =>
                string.Equals(value.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)));

            using var verifyRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"git/lfs-demo.git/info/lfs/objects/{oid}/verify");
            verifyRequest.Headers.Authorization = fixture.CreateAuthorizationHeader();
            verifyRequest.Content = new StringContent(
                $"{{\"oid\":\"{oid}\",\"size\":1}}",
                Encoding.UTF8,
                "application/vnd.git-lfs+json");
            using var verifyResponse = await protocolClient.SendAsync(verifyRequest);
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, verifyResponse.StatusCode);
        }

        Directory.Delete(Path.Combine(workTree, ".git", "lfs", "objects"), recursive: true);
        File.Delete(objectPath);
        await fixture.RunGitAsync(["-C", workTree, "lfs", "fetch", "origin", "main"], authenticate: true);
        await fixture.RunGitAsync(["-C", workTree, "checkout", "--", "payload.bin"], authenticate: true);
        CollectionAssert.AreEqual(content, await File.ReadAllBytesAsync(objectPath));

        await fixture.RunGitAsync([
            "-c", "protocol.version=2", "clone", $"{fixture.BaseAddress}git/lfs-demo.git", clonePath]);
        var clonedContent = await File.ReadAllBytesAsync(Path.Combine(clonePath, "payload.bin"));
        Assert.AreEqual(oid, Convert.ToHexString(SHA256.HashData(clonedContent)).ToLowerInvariant());
    }

    private sealed class GitLfsFixture : IAsyncDisposable
    {
        private const string OwnerName = "lfs-owner";
        private const string OwnerPassword = "M9-Lfs-Owner-Password-1!";

        private GitLfsFixture(WebApplication app, string baseAddress, string tempRoot)
        {
            App = app;
            BaseAddress = baseAddress;
            TempRoot = tempRoot;
        }

        public WebApplication App { get; }

        public string BaseAddress { get; }

        public string TempRoot { get; }

        public static async Task<GitLfsFixture> CreateAsync()
        {
            var tempRoot = TestDirectory.Create();
            var repositoryRoot = Path.Combine(tempRoot, "Repositories");
            var bareRepositoryPath = Path.Combine(repositoryRoot, "lfs-demo.git");
            Directory.CreateDirectory(repositoryRoot);
            await RunGitProcessAsync(["init", "--bare", "--initial-branch=main", bareRepositoryPath]);

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
                ["GitCandy:GitHttp:RequestTimeout"] = "00:05:00",
                ["GitCandy:Lfs:MaxObjectBytes"] = (LfsObjectSize * 2L).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["GitCandy:Lfs:OperationTimeout"] = "00:05:00"
            });
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            builder.Services.AddControllersWithViews()
                .AddApplicationPart(typeof(GitLfsController).Assembly);
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
                        UserName = OwnerName,
                        Email = "lfs-owner@example.com",
                        DisplayName = "LFS Owner"
                    };
                    var created = await userManager.CreateAsync(owner, OwnerPassword);
                    Assert.IsTrue(created.Succeeded, string.Join(", ", created.Errors.Select(error => error.Description)));

                    var repository = new GitCandyRepository
                    {
                        NamespaceId = GitCandyNamespace.LegacyNamespaceId,
                        Name = "lfs-demo",
                        StorageName = "lfs-demo",
                        Description = "M9 LFS integration fixture",
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
                    var privateRepository = new GitCandyRepository
                    {
                        NamespaceId = GitCandyNamespace.LegacyNamespaceId,
                        Name = "private-lfs",
                        StorageName = "private-lfs",
                        Description = "Private LFS authorization fixture",
                        CreatedAtUtc = DateTime.UtcNow,
                        IsPrivate = true
                    };
                    privateRepository.UserRoles.Add(new GitCandyUserRepositoryRole
                    {
                        UserId = owner.Id,
                        AllowRead = true,
                        AllowWrite = true,
                        IsOwner = true
                    });
                    dbContext.Repositories.Add(privateRepository);
                    await dbContext.SaveChangesAsync();
                }

                await app.StartAsync();
                var address = GetServerAddress(app);
                return new GitLfsFixture(
                    app,
                    address.EndsWith("/", StringComparison.Ordinal) ? address : $"{address}/",
                    tempRoot);
            }
            catch
            {
                await app.DisposeAsync();
                TestDirectory.Delete(tempRoot);
                throw;
            }
        }

        public Task RunGitAsync(IReadOnlyList<string> arguments, bool authenticate = false)
        {
            return RunGitProcessAsync(arguments, authenticate);
        }

        public async Task<string> RunGitForOutputAsync(IReadOnlyList<string> arguments)
        {
            return (await RunGitProcessForResultAsync(arguments)).Trim();
        }

        public AuthenticationHeaderValue CreateAuthorizationHeader()
        {
            return new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{OwnerName}:{OwnerPassword}")));
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
            TestDirectory.Delete(TempRoot);
        }

        private static async Task RunGitProcessAsync(
            IReadOnlyList<string> arguments,
            bool authenticate = false)
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
            if (authenticate)
            {
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{OwnerName}:{OwnerPassword}"));
                startInfo.Environment["GIT_CONFIG_COUNT"] = "1";
                startInfo.Environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
                startInfo.Environment["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {credentials}";
            }

            using var process = new Process { StartInfo = startInfo };
            Assert.IsTrue(process.Start());
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
            var output = await outputTask;
            var error = await errorTask;
            Assert.AreEqual(0, process.ExitCode, $"git {string.Join(' ', arguments)} failed:\n{output}\n{error}");
        }

        private static async Task<string> RunGitProcessForResultAsync(IReadOnlyList<string> arguments)
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

            using var process = new Process { StartInfo = startInfo };
            Assert.IsTrue(process.Start());
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(1));
            var output = await outputTask;
            var error = await errorTask;
            Assert.AreEqual(0, process.ExitCode, error);
            return output;
        }
    }

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
