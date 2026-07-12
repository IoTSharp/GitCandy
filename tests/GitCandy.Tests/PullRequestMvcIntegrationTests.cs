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
    public async Task RepositoryReferences_WithCanonicalRoutes_ListsDeletesAndProtectsPrivateData()
    {
        await using var fixture = await PullRequestWebFixture.CreateAsync();
        fixture.AddTags();

        using var branches = await fixture.Client.GetAsync("/review-author/reviews/branches");
        var branchesHtml = await branches.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, branches.StatusCode, branchesHtml);
        StringAssert.Contains(branchesHtml, "main");
        StringAssert.Contains(branchesHtml, "feature");

        using var tags = await fixture.Client.GetAsync("/review-author/reviews/tags");
        Assert.AreEqual(HttpStatusCode.OK, tags.StatusCode);
        var tagsHtml = await tags.Content.ReadAsStringAsync();
        StringAssert.Contains(tagsHtml, "v-lightweight");
        StringAssert.Contains(tagsHtml, "v-annotated");
        StringAssert.Contains(tagsHtml, "Annotated");

        using var contributors = await fixture.Client.GetAsync("/review-author/reviews/contributors?revision=main");
        Assert.AreEqual(HttpStatusCode.OK, contributors.StatusCode);
        var contributorsHtml = await contributors.Content.ReadAsStringAsync();
        StringAssert.Contains(contributorsHtml, "Contributors");
        Assert.IsFalse(contributorsHtml.Contains("@example.com", StringComparison.OrdinalIgnoreCase));
        using var invalidRevision = await fixture.Client.GetAsync("/review-author/reviews/contributors?revision=missing-ref");
        Assert.AreEqual(HttpStatusCode.NotFound, invalidRevision.StatusCode);

        await fixture.LoginAsync();
        var branchToken = await fixture.GetAntiforgeryTokenAsync("/review-author/reviews/branches");
        using var deleteFeature = await fixture.Client.PostAsync(
            "/review-author/reviews/branches/delete",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = branchToken,
                ["name"] = "feature"
            }));
        Assert.AreEqual(HttpStatusCode.Redirect, deleteFeature.StatusCode);
        Assert.IsFalse(fixture.ReferenceExists("refs/heads/feature"));

        using var deleteDefault = await fixture.Client.PostAsync(
            "/review-author/reviews/branches/delete",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = branchToken,
                ["name"] = "main"
            }));
        Assert.AreEqual(HttpStatusCode.Conflict, deleteDefault.StatusCode);
        Assert.IsTrue(fixture.ReferenceExists("refs/heads/main"));

        var tagToken = await fixture.GetAntiforgeryTokenAsync("/review-author/reviews/tags");
        using var deleteTag = await fixture.Client.PostAsync(
            "/review-author/reviews/tags/delete",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = tagToken,
                ["name"] = "v-lightweight"
            }));
        Assert.AreEqual(HttpStatusCode.Redirect, deleteTag.StatusCode);
        Assert.IsFalse(fixture.ReferenceExists("refs/tags/v-lightweight"));

        await fixture.MakeRepositoryPrivateAsync();
        using var anonymous = fixture.CreateClient();
        using var privateBranches = await anonymous.GetAsync("/review-author/reviews/branches");
        Assert.AreEqual(HttpStatusCode.Redirect, privateBranches.StatusCode);
    }

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
        StringAssert.Contains(filesHtml, "Add review comment");
        var reviewToken = AntiforgeryTokenRegex().Match(filesHtml).Groups[1].Value;
        using var review = await fixture.Client.PostAsync(
            "/review-author/reviews/pulls/1/review-threads",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = reviewToken,
                ["Path"] = "README.md",
                ["Side"] = "New",
                ["StartLine"] = "2",
                ["EndLine"] = "2",
                ["Body"] = "Check feature line."
            }));
        Assert.AreEqual(HttpStatusCode.Redirect, review.StatusCode);
        using var reviewedFiles = await fixture.Client.GetAsync("/review-author/reviews/pulls/1/files");
        var reviewedFilesHtml = await reviewedFiles.Content.ReadAsStringAsync();
        StringAssert.Contains(reviewedFilesHtml, "Check feature line.");
        StringAssert.Contains(reviewedFilesHtml, "Resolve");

        var detailToken = await fixture.GetAntiforgeryTokenAsync("/review-author/reviews/pulls/1");
        using var requestReview = await fixture.Client.PostAsync(
            "/review-author/reviews/pulls/1/reviewers",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = detailToken,
                ["reviewerUserId"] = fixture.ReviewerId
            }));
        Assert.AreEqual(HttpStatusCode.Redirect, requestReview.StatusCode);

        using var reviewerClient = fixture.CreateClient();
        await fixture.LoginAsync(reviewerClient, "reviewer");
        var reviewerToken = await fixture.GetAntiforgeryTokenAsync(
            reviewerClient,
            "/review-author/reviews/pulls/1");
        using var approve = await reviewerClient.PostAsync(
            "/review-author/reviews/pulls/1/reviews",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = reviewerToken,
                ["State"] = "Approved",
                ["Body"] = "Reviewed through MVC."
            }));
        Assert.AreEqual(HttpStatusCode.Redirect, approve.StatusCode);
        using var approvedDetail = await reviewerClient.GetAsync("/review-author/reviews/pulls/1");
        var approvedHtml = await approvedDetail.Content.ReadAsStringAsync();
        StringAssert.Contains(approvedHtml, "Approved");
        StringAssert.Contains(approvedHtml, "Reviewed through MVC.");

        var authorToken = await fixture.GetAntiforgeryTokenAsync("/review-author/reviews/pulls/1/files");
        using var resolve = await fixture.Client.PostAsync(
            "/review-author/reviews/pulls/1/review-threads/1/resolved",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = authorToken,
                ["resolved"] = "true"
            }));
        Assert.AreEqual(HttpStatusCode.Redirect, resolve.StatusCode);
        authorToken = await fixture.GetAntiforgeryTokenAsync("/review-author/reviews/pulls/1");
        using var ready = await fixture.Client.PostAsync(
            "/review-author/reviews/pulls/1/draft",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = authorToken,
                ["isDraft"] = "false"
            }));
        Assert.AreEqual(HttpStatusCode.Redirect, ready.StatusCode);
        using var mergePage = await fixture.Client.GetAsync("/review-author/reviews/pulls/1");
        var mergeHtml = await mergePage.Content.ReadAsStringAsync();
        StringAssert.Contains(mergeHtml, "Mergeable");
        var version = PullRequestVersionRegex().Match(mergeHtml);
        Assert.IsTrue(version.Success);
        authorToken = AntiforgeryTokenRegex().Match(mergeHtml).Groups[1].Value;
        using var merge = await fixture.Client.PostAsync(
            "/review-author/reviews/pulls/1/merge",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = authorToken,
                ["Method"] = "Squash",
                ["Message"] = "Squash reviewed feature",
                ["Version"] = version.Groups[1].Value
            }));
        Assert.AreEqual(HttpStatusCode.Redirect, merge.StatusCode);
        using var mergedDetail = await fixture.Client.GetAsync("/review-author/reviews/pulls/1");
        StringAssert.Contains(await mergedDetail.Content.ReadAsStringAsync(), "Merged");
        fixture.AssertTargetBranchMerged(expectedParentCount: 1);

        await fixture.MakeRepositoryPrivateAsync();
        using var anonymousClient = fixture.CreateClient();
        using var denied = await anonymousClient.GetAsync("/review-author/reviews/pulls/1");
        Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode);
        using var deniedFiles = await anonymousClient.GetAsync("/review-author/reviews/pulls/1/files");
        Assert.AreEqual(HttpStatusCode.NotFound, deniedFiles.StatusCode);
    }

    private sealed class PullRequestWebFixture : IAsyncDisposable
    {
        private const string Password = "M12-Pull-Request-2026!";

        private PullRequestWebFixture(
            WebApplication app,
            HttpClient client,
            string tempRoot,
            string repositoryPath,
            string reviewerId)
        {
            App = app;
            Client = client;
            TempRoot = tempRoot;
            RepositoryPath = repositoryPath;
            ReviewerId = reviewerId;
        }

        private WebApplication App { get; }
        public HttpClient Client { get; }
        private string TempRoot { get; }
        private string RepositoryPath { get; }
        public string ReviewerId { get; }

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
                var reviewerId = await SeedAsync(app);
                await app.StartAsync();
                return new PullRequestWebFixture(app, CreateClient(app), tempRoot, repositoryPath, reviewerId);
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
            await LoginAsync(Client, "review-author");
        }

        public async Task LoginAsync(HttpClient client, string userName)
        {
            var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");
            using var response = await client.PostAsync(
                "/Account/Login",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["UserNameOrEmail"] = userName,
                    ["Password"] = Password,
                    ["RememberMe"] = "false"
                }));
            Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        }

        public async Task<string> GetAntiforgeryTokenAsync(string path)
        {
            return await GetAntiforgeryTokenAsync(Client, path);
        }

        public async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
        {
            var html = await client.GetStringAsync(path);
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

        public void AssertTargetBranchMerged(int expectedParentCount)
        {
            using var repository = new Repository(RepositoryPath);
            var commit = repository.Branches["main"]?.Tip;
            Assert.IsNotNull(commit);
            Assert.AreEqual(expectedParentCount, commit.Parents.Count());
            StringAssert.Contains(commit.Message, "Squash reviewed feature");
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

        public void AddTags()
        {
            using var repository = new Repository(RepositoryPath);
            var target = repository.Branches["main"].Tip;
            var signature = new Signature("Release Bot", "release@example.com", DateTimeOffset.UtcNow);
            repository.ApplyTag("v-lightweight", target.Sha);
            repository.ApplyTag("v-annotated", target.Sha, signature, "Annotated release");
        }

        public bool ReferenceExists(string canonicalName)
        {
            using var repository = new Repository(RepositoryPath);
            return repository.Refs[canonicalName] is not null;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            TestDirectory.Delete(TempRoot);
        }

        private static async Task<string> SeedAsync(WebApplication app)
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
            var reviewer = new GitCandyUser
            {
                UserName = "reviewer",
                Email = "reviewer@example.com"
            };
            result = await userManager.CreateAsync(reviewer, Password);
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
            repository.UserRoles.Add(new GitCandyUserRepositoryRole
            {
                UserId = reviewer.Id,
                AllowRead = true,
                AllowWrite = false,
                IsOwner = false
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
            return reviewer.Id;
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
                bare.Refs.UpdateTarget("HEAD", "refs/heads/main");
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

    [GeneratedRegex("name=\"Version\" value=\"([0-9]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex PullRequestVersionRegex();
}
