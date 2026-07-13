using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using GitCandy.Application;
using GitCandy.Configuration;
using GitCandy.Controllers;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Workspace;
using GitCandy.Governance;
using GitCandy.Credentials;
using GitCandy.Integrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;

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

        await fixture.RunGitAsync(
            ["-c", "protocol.version=2", "clone", $"{fixture.BaseAddress}m6-owner/public-demo.git", clonePath]);
        Assert.IsTrue(File.Exists(Path.Combine(clonePath, "README.md")));

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
        Assert.IsTrue(await fixture.HasPushActivityAsync(), "Successful HTTP receive-pack must publish a shared workspace activity event.");
        Assert.IsTrue(await fixture.HasPushIntegrationEventAsync(), "Successful HTTP receive-pack must enqueue a versioned integration event.");
    }

    [TestMethod]
    public async Task GitSmartHttp_WithLegacyOrAliasAddress_ReturnsNotFound()
    {
        await using var fixture = await GitHttpFixture.CreateAsync();
        using var client = new HttpClient { BaseAddress = new Uri(fixture.BaseAddress) };
        using var legacyResponse = await client.GetAsync(
            "git/public-demo.git/info/refs?service=git-upload-pack");
        using var noSuffixResponse = await client.GetAsync(
            "m6-owner/public-demo/info/refs?service=git-upload-pack");
        using var aliasResponse = await client.GetAsync(
            "m6-previous/public-old.git/info/refs?service=git-upload-pack");

        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, legacyResponse.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, noSuffixResponse.StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, aliasResponse.StatusCode);
    }

    [TestMethod]
    public async Task GitSmartHttp_WithProtectedMain_RejectsReceivePackBeforeRefUpdate()
    {
        await using var fixture = await GitHttpFixture.CreateAsync();
        var clonePath = Path.Combine(fixture.TempRoot, "clones", "protected-main");
        await fixture.RunGitAsync(["clone", $"{fixture.BaseAddress}m6-owner/public-demo.git", clonePath]);
        await fixture.RunGitAsync(["-C", clonePath, "config", "user.name", "Gate Test"]);
        await fixture.RunGitAsync(["-C", clonePath, "config", "user.email", "gate@example.com"]);
        File.AppendAllText(Path.Combine(clonePath, "README.md"), "blocked push\n", Encoding.UTF8);
        await fixture.RunGitAsync(["-C", clonePath, "add", "README.md"]);
        await fixture.RunGitAsync(["-C", clonePath, "commit", "-m", "blocked protected push"]);
        var originalHead = await fixture.RunGitForOutputAsync(
            ["--git-dir", fixture.BareRepositoryPath, "rev-parse", "refs/heads/main"]);
        await fixture.ProtectMainAsync();

        var push = await fixture.RunGitExpectFailureAsync(
            ["-C", clonePath, "push", "origin", "HEAD:refs/heads/main"],
            useOwnerCredentials: true);

        StringAssert.Contains(push.StandardError, "GitCandy push rejected");
        var currentHead = await fixture.RunGitForOutputAsync(
            ["--git-dir", fixture.BareRepositoryPath, "rev-parse", "refs/heads/main"]);
        Assert.AreEqual(originalHead, currentHead);
    }

    [TestMethod]
    public async Task GitSmartHttp_WithPersonalAccessToken_SeparatesReadAndWriteScopes()
    {
        await using var fixture = await GitHttpFixture.CreateAsync();
        var clonePath = Path.Combine(fixture.TempRoot, "clones", "pat-scopes");
        var readToken = await fixture.CreatePersonalAccessTokenAsync(PersonalAccessTokenScopes.GitRead);
        await fixture.RunGitWithTokenAsync(
            ["clone", $"{fixture.BaseAddress}m6-owner/public-demo.git", clonePath],
            readToken);
        await fixture.RunGitAsync(["-C", clonePath, "config", "user.name", "PAT Test"]);
        await fixture.RunGitAsync(["-C", clonePath, "config", "user.email", "pat@example.com"]);
        File.AppendAllText(Path.Combine(clonePath, "README.md"), "pat scopes\n", Encoding.UTF8);
        await fixture.RunGitAsync(["-C", clonePath, "add", "README.md"]);
        await fixture.RunGitAsync(["-C", clonePath, "commit", "-m", "PAT scope test"]);

        var denied = await fixture.RunGitWithTokenExpectFailureAsync(
            ["-C", clonePath, "push", "origin", "HEAD:refs/heads/pat-scope"],
            readToken);
        Assert.IsTrue(
            denied.StandardError.Contains("403", StringComparison.Ordinal)
            || denied.StandardError.Contains("forbidden", StringComparison.OrdinalIgnoreCase));

        var writeToken = await fixture.CreatePersonalAccessTokenAsync(PersonalAccessTokenScopes.GitWrite);
        await fixture.RunGitWithTokenAsync(
            ["-C", clonePath, "push", "origin", "HEAD:refs/heads/pat-scope"],
            writeToken);
        var remote = await fixture.RunGitForOutputAsync(
            ["--git-dir", fixture.BareRepositoryPath, "rev-parse", "refs/heads/pat-scope"]);
        Assert.AreEqual(
            await fixture.RunGitForOutputAsync(["-C", clonePath, "rev-parse", "HEAD"]),
            remote);
    }

    [TestMethod]
    public async Task CommitCheckApi_WithPatScopes_UpsertsExactShaAndRejectsBlockedTarget()
    {
        await using var fixture = await GitHttpFixture.CreateAsync();
        var sha = await fixture.RunGitForOutputAsync(
            ["--git-dir", fixture.BareRepositoryPath, "rev-parse", "refs/heads/main"]);
        var readToken = await fixture.CreatePersonalAccessTokenAsync(PersonalAccessTokenScopes.ApiRead);
        var writeToken = await fixture.CreatePersonalAccessTokenAsync(PersonalAccessTokenScopes.ApiWrite);
        using var client = new HttpClient { BaseAddress = new Uri(fixture.BaseAddress) };
        var path = $"api/v1/repositories/m6-owner/public-demo/commits/{sha}/statuses";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);
        using var denied = await client.PostAsJsonAsync(path, new
        {
            context = "ci/build",
            state = "success",
            description = "passed"
        });
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", writeToken);
        using var pending = await client.PostAsJsonAsync(path, new
        {
            context = "ci/build",
            state = "pending",
            description = "running",
            externalId = "build-42"
        });
        Assert.AreEqual(HttpStatusCode.OK, pending.StatusCode);
        using var success = await client.PostAsJsonAsync(path, new
        {
            context = "ci/build",
            state = "success",
            description = "passed",
            externalId = "build-42"
        });
        Assert.AreEqual(HttpStatusCode.OK, success.StatusCode);
        Assert.AreEqual(1, await fixture.CountCommitChecksAsync(sha));
        using var checkRun = await client.PostAsJsonAsync(
            $"api/v1/repositories/m6-owner/public-demo/commits/{sha}/checks",
            new
            {
                name = "security/scan",
                state = "in_progress",
                summary = "scanning",
                externalId = "scan-42"
            });
        Assert.AreEqual(HttpStatusCode.OK, checkRun.StatusCode);
        Assert.AreEqual(2, await fixture.CountCommitChecksAsync(sha));

        using var blockedTarget = await client.PostAsJsonAsync(path, new
        {
            context = "security/scan",
            state = "success",
            targetUrl = "http://127.0.0.1/internal"
        });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, blockedTarget.StatusCode);
        using var stale = await client.PostAsJsonAsync(
            $"api/v1/repositories/m6-owner/public-demo/commits/{new string('f', 40)}/statuses",
            new { context = "ci/build", state = "success" });
        Assert.AreEqual(HttpStatusCode.NotFound, stale.StatusCode);
        var rateLimited = false;
        for (var attempt = 0; attempt < 130; attempt++)
        {
            using var response = await client.PostAsJsonAsync(path, new { });
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimited = true;
                break;
            }
        }
        Assert.IsTrue(rateLimited, "API write requests must be rate limited per credential.");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);
        using var get = await client.GetAsync(
            $"api/v1/repositories/m6-owner/public-demo/commits/{sha}/checks");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        StringAssert.Contains(await get.Content.ReadAsStringAsync(), "\"state\":\"success\"");
    }

    [TestMethod]
    public async Task GitSmartHttp_WithRequiredCheck_AllowsCheckedShaAndRejectsNextHead()
    {
        await using var fixture = await GitHttpFixture.CreateAsync();
        var clonePath = Path.Combine(fixture.TempRoot, "clones", "required-check");
        await fixture.RunGitAsync(["clone", $"{fixture.BaseAddress}m6-owner/public-demo.git", clonePath]);
        await fixture.RunGitAsync(["-C", clonePath, "config", "user.name", "CI Gate Test"]);
        await fixture.RunGitAsync(["-C", clonePath, "config", "user.email", "ci-gate@example.com"]);
        File.AppendAllText(Path.Combine(clonePath, "README.md"), "checked head\n", Encoding.UTF8);
        await fixture.RunGitAsync(["-C", clonePath, "add", "README.md"]);
        await fixture.RunGitAsync(["-C", clonePath, "commit", "-m", "checked head"]);
        await fixture.RunGitAsync(
            ["-C", clonePath, "push", "origin", "HEAD:refs/heads/ci-candidate"],
            useOwnerCredentials: true);
        var checkedSha = await fixture.RunGitForOutputAsync(["-C", clonePath, "rev-parse", "HEAD"]);
        await fixture.ProtectMainWithRequiredCheckAsync("ci/build");
        var token = await fixture.CreatePersonalAccessTokenAsync(PersonalAccessTokenScopes.ApiWrite);
        Assert.AreEqual(HttpStatusCode.OK, await fixture.PostStatusAsync(checkedSha, token, "success"));

        await fixture.RunGitAsync(
            ["-C", clonePath, "push", "origin", "HEAD:refs/heads/main"],
            useOwnerCredentials: true);
        File.AppendAllText(Path.Combine(clonePath, "README.md"), "unchecked head\n", Encoding.UTF8);
        await fixture.RunGitAsync(["-C", clonePath, "add", "README.md"]);
        await fixture.RunGitAsync(["-C", clonePath, "commit", "-m", "unchecked head"]);
        await fixture.RunGitAsync(
            ["-C", clonePath, "push", "origin", "HEAD:refs/heads/ci-candidate"],
            useOwnerCredentials: true);
        var rejected = await fixture.RunGitExpectFailureAsync(
            ["-C", clonePath, "push", "origin", "HEAD:refs/heads/main"],
            useOwnerCredentials: true);

        StringAssert.Contains(rejected.StandardError, "required check 'ci/build' is missing");
        Assert.AreEqual(
            checkedSha,
            await fixture.RunGitForOutputAsync(
                ["--git-dir", fixture.BareRepositoryPath, "rev-parse", "refs/heads/main"]));
    }

    private static async Task WriteRandomFileAsync(string path, int size)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The file path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);
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
            string tempRoot,
            long repositoryId)
        {
            App = app;
            BaseAddress = baseAddress;
            BareRepositoryPath = bareRepositoryPath;
            SeedWorkTree = seedWorkTree;
            TempRoot = tempRoot;
            RepositoryId = repositoryId;
        }

        private WebApplication App { get; }

        public string BaseAddress { get; }

        public string BareRepositoryPath { get; }

        public string SeedWorkTree { get; }

        public string TempRoot { get; }

        public long RepositoryId { get; }

        public async Task ProtectMainAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            var ownerId = await db.Users.Where(user => user.NormalizedUserName == "M6-OWNER")
                .Select(user => user.Id)
                .SingleAsync();
            var saved = await scope.ServiceProvider.GetRequiredService<IBranchProtectionService>().SaveAsync(
                RepositoryId,
                ownerId,
                new BranchProtectionEdit(
                    null,
                    "main",
                    BranchAccessLevel.Nobody,
                    BranchAccessLevel.RepositoryOwner,
                    AllowForcePushes: false,
                    AllowDeletions: false,
                    AllowAdministratorBypass: false));
            Assert.IsNotNull(saved);
        }

        public async Task ProtectMainWithRequiredCheckAsync(string context)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            var ownerId = await db.Users.Where(user => user.NormalizedUserName == "M6-OWNER")
                .Select(user => user.Id)
                .SingleAsync();
            var saved = await scope.ServiceProvider.GetRequiredService<IBranchProtectionService>().SaveAsync(
                RepositoryId,
                ownerId,
                new BranchProtectionEdit(
                    null,
                    "main",
                    BranchAccessLevel.RepositoryOwner,
                    BranchAccessLevel.RepositoryOwner,
                    AllowForcePushes: false,
                    AllowDeletions: false,
                    AllowAdministratorBypass: false,
                    RequiredChecks: [context]));
            Assert.IsNotNull(saved);
        }

        public async Task<string> CreatePersonalAccessTokenAsync(string scopeName)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var ownerId = await scope.ServiceProvider.GetRequiredService<GitCandyDbContext>().Users
                .Where(user => user.NormalizedUserName == "M6-OWNER")
                .Select(user => user.Id)
                .SingleAsync();
            var token = await scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>().CreateAsync(
                ownerId,
                "integration token",
                [scopeName],
                DateTimeOffset.UtcNow.AddHours(1));
            Assert.IsNotNull(token);
            return token.Token;
        }

        public async Task<bool> HasPushActivityAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            return await dbContext.ActivityEvents.AsNoTracking().AnyAsync(item => item.Type == WorkspaceActivityType.Push);
        }

        public async Task<bool> HasPushIntegrationEventAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            return await dbContext.IntegrationEvents.AsNoTracking().AnyAsync(item => item.Type == "push" && item.SchemaVersion == 1);
        }

        public async Task<int> CountCommitChecksAsync(string sha)
        {
            await using var scope = App.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<GitCandyDbContext>().CommitChecks.AsNoTracking()
                .CountAsync(item => item.RepositoryId == RepositoryId && item.Sha == sha);
        }

        public async Task<HttpStatusCode> PostStatusAsync(string sha, string token, string state)
        {
            using var client = new HttpClient { BaseAddress = new Uri(BaseAddress) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.PostAsJsonAsync(
                $"api/v1/repositories/m6-owner/public-demo/commits/{sha}/statuses",
                new { context = "ci/build", state, description = "fixture" });
            return response.StatusCode;
        }

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
            app.UseRateLimiter();
            app.MapGitCandyCompatibilityRoutes();

            long repositoryId = 0;
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

                    var namespaceService = scope.ServiceProvider.GetRequiredService<INamespaceProvisioningService>();
                    var namespaceId = await namespaceService.EnsureUserNamespaceAsync(owner.Id);
                    Assert.IsNotNull(namespaceId);

                    var repository = new GitCandyRepository
                    {
                        NamespaceId = namespaceId.Value,
                        Name = "public-demo",
                        StorageName = "public-demo",
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
                    repositoryId = repository.Id;
                    var utcNow = DateTime.UtcNow;
                    var namespaceAlias = new GitCandyNamespaceAlias
                    {
                        NamespaceId = namespaceId.Value,
                        Slug = "m6-previous",
                        CreatedAtUtc = utcNow,
                        ExpiresAtUtc = utcNow.AddDays(365)
                    };
                    var repositoryAlias = new GitCandyRepositoryAlias
                    {
                        NamespaceId = namespaceId.Value,
                        RepositoryId = repository.Id,
                        Slug = "public-old",
                        CreatedAtUtc = utcNow,
                        ExpiresAtUtc = utcNow.AddDays(365)
                    };
                    dbContext.NamespaceAliases.Add(namespaceAlias);
                    dbContext.RepositoryAliases.Add(repositoryAlias);
                    await dbContext.SaveChangesAsync();
                    dbContext.NamespaceClaims.Add(new GitCandyNamespaceClaim
                    {
                        NormalizedSlug = "M6-PREVIOUS",
                        Slug = namespaceAlias.Slug,
                        ClaimType = NameClaimType.Alias,
                        NamespaceAliasId = namespaceAlias.Id
                    });
                    dbContext.RepositoryClaims.AddRange(
                        new GitCandyRepositoryClaim
                        {
                            NamespaceId = namespaceId.Value,
                            NormalizedSlug = "PUBLIC-DEMO",
                            Slug = repository.Name,
                            ClaimType = NameClaimType.Current,
                            RepositoryId = repository.Id
                        },
                        new GitCandyRepositoryClaim
                        {
                            NamespaceId = namespaceId.Value,
                            NormalizedSlug = "PUBLIC-OLD",
                            Slug = repositoryAlias.Slug,
                            ClaimType = NameClaimType.Alias,
                            RepositoryAliasId = repositoryAlias.Id
                        });
                    dbContext.LegacyRepositoryRoutes.Add(new GitCandyLegacyRepositoryRoute
                    {
                        Project = repository.Name,
                        NormalizedProject = repository.NormalizedName,
                        RepositoryId = repository.Id,
                        CreatedAtUtc = utcNow
                    });
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
                    tempRoot,
                    repositoryId);
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

        public Task<GitProcessResult> RunGitExpectFailureAsync(
            IReadOnlyList<string> arguments,
            bool useOwnerCredentials = false)
        {
            return RunGitProcessAsync(arguments, useOwnerCredentials, expectSuccess: false);
        }

        public Task RunGitWithTokenAsync(IReadOnlyList<string> arguments, string token)
        {
            return RunGitProcessAsync(arguments, credentialSecret: token);
        }

        public Task<GitProcessResult> RunGitWithTokenExpectFailureAsync(
            IReadOnlyList<string> arguments,
            string token)
        {
            return RunGitProcessAsync(arguments, expectSuccess: false, credentialSecret: token);
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
            TestDirectory.Delete(TempRoot);
        }

        private static async Task<GitProcessResult> RunGitProcessAsync(
            IReadOnlyList<string> arguments,
            bool useOwnerCredentials = false,
            bool expectSuccess = true,
            string? credentialSecret = null)
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

            if (useOwnerCredentials || credentialSecret is not null)
            {
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{OwnerUserName}:{credentialSecret ?? OwnerPassword}"));
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
            if (expectSuccess)
            {
                Assert.AreEqual(
                    0,
                    process.ExitCode,
                    $"git {arguments.FirstOrDefault()} failed: {standardError}");
            }
            else
            {
                Assert.AreNotEqual(0, process.ExitCode, "The protected push unexpectedly succeeded.");
            }
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
