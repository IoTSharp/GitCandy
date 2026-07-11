using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GitCandy.Configuration;
using GitCandy.Controllers;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using LibGit2Sharp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GitCandy.Tests;

[TestClass]
public sealed partial class PullRequestMvcIntegrationTests
{
    [TestMethod]
    public async Task PullRequestMvc_WithWritableBranches_CreatesDraftAndHidesPrivateRepository()
    {
        await using var fixture = await PullRequestWebFixture.CreateAsync();
        using var anonymousList = await fixture.Client.GetAsync("/review-author/reviews/pulls");
        Assert.AreEqual(HttpStatusCode.OK, anonymousList.StatusCode);
        StringAssert.Contains(await anonymousList.Content.ReadAsStringAsync(), "No open pull requests");

        await fixture.LoginAsync();
        var token = await fixture.GetAntiforgeryTokenAsync("/review-author/reviews/pulls/new");
        using var create = await fixture.Client.PostAsync(
            "/review-author/reviews/pulls/new",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Title"] = "Review feature",
                ["Body"] = "```csharp\nvar reviewed = true;\n```",
                ["SourceBranch"] = "feature",
                ["TargetBranch"] = "main",
                ["IsDraft"] = "true"
            }));
        Assert.AreEqual(HttpStatusCode.Redirect, create.StatusCode);
        Assert.IsNotNull(create.Headers.Location);
        using var detail = await fixture.Client.GetAsync(create.Headers.Location);
        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
        var html = await detail.Content.ReadAsStringAsync();
        StringAssert.Contains(html, "Review feature");
        StringAssert.Contains(html, "Draft");
        StringAssert.Contains(html, "feature");
        fixture.AssertPullRequestHeadExists(1);

        using var commits = await fixture.Client.GetAsync("/review-author/reviews/pulls/1/commits");
        Assert.AreEqual(HttpStatusCode.OK, commits.StatusCode);
        var commitsHtml = await commits.Content.ReadAsStringAsync();
        StringAssert.Contains(commitsHtml, "Feature");
        var commitLink = PullRequestCommitLinkRegex().Match(commitsHtml);
        Assert.IsTrue(commitLink.Success);
        using var commit = await fixture.Client.GetAsync(WebUtility.HtmlDecode(commitLink.Groups[1].Value));
        Assert.AreEqual(HttpStatusCode.OK, commit.StatusCode);
        StringAssert.Contains(await commit.Content.ReadAsStringAsync(), "Feature");

        using var files = await fixture.Client.GetAsync("/review-author/reviews/pulls/1/files");
        Assert.AreEqual(HttpStatusCode.OK, files.StatusCode);
        var filesHtml = await files.Content.ReadAsStringAsync();
        StringAssert.Contains(filesHtml, "README.md");
        StringAssert.Contains(filesHtml, "language-diff");

        await fixture.MakeRepositoryPrivateAsync();
        using var anonymousClient = fixture.CreateClient();
        using var denied = await anonymousClient.GetAsync("/review-author/reviews/pulls/1");
        Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode);
    }

    private sealed class PullRequestWebFixture : IAsyncDisposable
    {
        private const string Password = "M12-Pull-Request-2026!";

        private PullRequestWebFixture(
            WebApplication app,
            HttpClient client,
            string tempRoot,
            string repositoryPath)
        {
            App = app;
            Client = client;
            TempRoot = tempRoot;
            RepositoryPath = repositoryPath;
        }

        private WebApplication App { get; }
        public HttpClient Client { get; }
        private string TempRoot { get; }
        private string RepositoryPath { get; }

        public static async Task<PullRequestWebFixture> CreateAsync()
        {
            var tempRoot = TestDirectory.Create();
            var repositoriesPath = Path.Combine(tempRoot, "Repositories");
            Directory.CreateDirectory(repositoriesPath);
            var repositoryPath = CreateBareRepository(tempRoot, repositoriesPath);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = GetWebProjectRoot(),
                EnvironmentName = Environments.Development
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "sqlite",
                ["ConnectionStrings:GitCandy"] = $"Data Source={Path.Combine(tempRoot, "GitCandy.db")};Pooling=False",
                ["GitCandy:Application:RepositoryPath"] = repositoriesPath,
                ["GitCandy:Application:CachePath"] = Path.Combine(tempRoot, "Caches"),
                ["GitCandy:Application:DataProtectionKeysPath"] = Path.Combine(tempRoot, "Keys"),
                ["GitCandy:Application:EnableSsh"] = "false"
            });
            builder.Services.AddGitCandyWebShell(
                builder.Configuration,
                builder.Environment.ContentRootPath);
            builder.Services.AddControllersWithViews()
                .AddApplicationPart(typeof(PullRequestController).Assembly);
            builder.Services.RemoveAll<IHostedService>();
            builder.Services.ConfigureApplicationCookie(
                options => options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);
            var app = builder.Build();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGitCandyCompatibilityRoutes();
            try
            {
                await SeedAsync(app);
                await app.StartAsync();
                return new PullRequestWebFixture(app, CreateClient(app), tempRoot, repositoryPath);
            }
            catch
            {
                await app.DisposeAsync();
                TestDirectory.Delete(tempRoot);
                throw;
            }
        }

        public HttpClient CreateClient() => CreateClient(App);

        public async Task LoginAsync()
        {
            var token = await GetAntiforgeryTokenAsync("/Account/Login");
            using var response = await Client.PostAsync(
                "/Account/Login",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["UserNameOrEmail"] = "review-author",
                    ["Password"] = Password,
                    ["RememberMe"] = "false"
                }));
            Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        }

        public async Task<string> GetAntiforgeryTokenAsync(string path)
        {
            var html = await Client.GetStringAsync(path);
            var match = AntiforgeryTokenRegex().Match(html);
            Assert.IsTrue(match.Success);
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }

        public void AssertPullRequestHeadExists(long number)
        {
            using var repository = new Repository(RepositoryPath);
            Assert.IsNotNull(repository.Refs[$"refs/pull/{number}/head"]);
            Assert.AreEqual(
                "refs/pull/",
                repository.Config.Get<string>("receive.hideRefs")?.Value);
        }

        public async Task MakeRepositoryPrivateAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            var repository = await dbContext.Repositories.SingleAsync(item => item.Name == "reviews");
            repository.IsPrivate = true;
            repository.AllowAnonymousRead = false;
            await dbContext.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            TestDirectory.Delete(TempRoot);
        }

        private static async Task SeedAsync(WebApplication app)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.MigrateAsync();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
            var user = new GitCandyUser
            {
                UserName = "review-author",
                Email = "review-author@example.com"
            };
            var result = await userManager.CreateAsync(user, Password);
            Assert.IsTrue(
                result.Succeeded,
                string.Join(Environment.NewLine, result.Errors.Select(error => error.Description)));
            var repositoryNamespace = new GitCandyNamespace
            {
                OwnerType = NamespaceOwnerType.User,
                UserId = user.Id,
                Slug = "review-author",
                CreatedAtUtc = DateTime.UtcNow
            };
            var repository = new GitCandyRepository
            {
                Namespace = repositoryNamespace,
                Name = "reviews",
                StorageName = "reviews",
                Description = string.Empty,
                CreatedAtUtc = DateTime.UtcNow,
                AllowAnonymousRead = true,
                WorkItemSequence = new GitCandyWorkItemSequence()
            };
            repository.UserRoles.Add(new GitCandyUserRepositoryRole
            {
                UserId = user.Id,
                AllowRead = true,
                AllowWrite = true,
                IsOwner = true
            });
            dbContext.Repositories.Add(repository);
            await dbContext.SaveChangesAsync();
            dbContext.NamespaceClaims.Add(new GitCandyNamespaceClaim
            {
                NormalizedSlug = "REVIEW-AUTHOR",
                Slug = "review-author",
                ClaimType = NameClaimType.Current,
                NamespaceId = repositoryNamespace.Id
            });
            dbContext.RepositoryClaims.Add(new GitCandyRepositoryClaim
            {
                NamespaceId = repositoryNamespace.Id,
                NormalizedSlug = "REVIEWS",
                Slug = "reviews",
                ClaimType = NameClaimType.Current,
                RepositoryId = repository.Id
            });
            await dbContext.SaveChangesAsync();
        }

        private static string CreateBareRepository(string tempRoot, string repositoriesPath)
        {
            var workPath = Path.Combine(tempRoot, "work");
            Repository.Init(workPath);
            string mainSha;
            string featureSha;
            using (var repository = new Repository(workPath))
            {
                var signature = new Signature(
                    "GitCandy Review",
                    "review@gitcandy.local",
                    new DateTimeOffset(2026, 7, 11, 10, 0, 0, TimeSpan.Zero));
                File.WriteAllText(Path.Combine(workPath, "README.md"), "main\n", Encoding.UTF8);
                Commands.Stage(repository, "README.md");
                var initial = repository.Commit("Initial", signature, signature);
                mainSha = initial.Id.Sha;
                var main = repository.CreateBranch("main", initial);
                Commands.Checkout(repository, main);
                var feature = repository.CreateBranch("feature", initial);
                Commands.Checkout(repository, feature);
                File.AppendAllText(Path.Combine(workPath, "README.md"), "feature\n", Encoding.UTF8);
                Commands.Stage(repository, "README.md");
                featureSha = repository.Commit("Feature", signature, signature).Id.Sha;
            }

            var barePath = Path.Combine(repositoriesPath, "reviews.git");
            Repository.Clone(workPath, barePath, new CloneOptions { IsBare = true });
            using (var bare = new Repository(barePath))
            {
                SetReference(bare, "refs/heads/main", mainSha);
                SetReference(bare, "refs/heads/feature", featureSha);
                bare.Refs.UpdateTarget(bare.Refs.Head, "refs/heads/main");
            }

            return barePath;
        }

        private static void SetReference(Repository repository, string name, string targetSha)
        {
            var reference = repository.Refs[name];
            if (reference is null)
            {
                repository.Refs.Add(name, targetSha);
            }
            else
            {
                repository.Refs.UpdateTarget(reference, targetSha);
            }
        }

        private static HttpClient CreateClient(WebApplication app)
        {
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses;
            return new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true
            })
            {
                BaseAddress = new Uri(addresses.Single())
            };
        }

        private static string GetWebProjectRoot()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory is not null
                && !File.Exists(Path.Combine(directory.FullName, "GitCandy.slnx")))
            {
                directory = directory.Parent;
            }

            directory ??= new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null
                && !File.Exists(Path.Combine(directory.FullName, "GitCandy.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory);
            return Path.Combine(directory.FullName, "src", "GitCandy");
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenRegex();

    [GeneratedRegex("href=\"([^\"]+/pulls/1/commits/[0-9a-f]{40})\"", RegexOptions.CultureInvariant)]
    private static partial Regex PullRequestCommitLinkRegex();
}
