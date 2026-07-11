using System.Net;
using System.Text.RegularExpressions;
using GitCandy.Configuration;
using GitCandy.Controllers;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
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
using Microsoft.AspNetCore.Routing;

namespace GitCandy.Tests;

[TestClass]
public sealed partial class IssueMvcIntegrationTests
{
    [TestMethod]
    public async Task IssueMvc_WithAuthenticatedAuthor_CompletesCreateListDetailAndPrivateDenial()
    {
        await using var fixture = await IssueWebFixture.CreateAsync();
        using var anonymousList = await fixture.Client.GetAsync("/issue-author/issues/issues");
        Assert.AreEqual(HttpStatusCode.OK, anonymousList.StatusCode, fixture.DescribeIssueRoutes());
        StringAssert.Contains(await anonymousList.Content.ReadAsStringAsync(), "No open issues");

        await fixture.LoginAsync();
        var token = await fixture.GetAntiforgeryTokenAsync("/issue-author/issues/issues/new");
        using var create = await fixture.Client.PostAsync("/issue-author/issues/issues/new", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Title"] = "MVC issue",
            ["Body"] = "```csharp\nvar ok = true;\n```\n<script>alert(1)</script>"
        }));
        Assert.AreEqual(HttpStatusCode.Redirect, create.StatusCode);
        Assert.IsNotNull(create.Headers.Location);
        using var detail = await fixture.Client.GetAsync(create.Headers.Location);
        Assert.AreEqual(HttpStatusCode.OK, detail.StatusCode);
        var html = await detail.Content.ReadAsStringAsync();
        StringAssert.Contains(html, "MVC issue");
        StringAssert.Contains(html, "language-csharp");
        Assert.IsFalse(html.Contains("<script>alert(1)</script>", StringComparison.OrdinalIgnoreCase));

        await fixture.MakeRepositoryPrivateAsync();
        using var otherClient = fixture.CreateClient();
        using var denied = await otherClient.GetAsync("/issue-author/issues/issues/1");
        Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode);
    }

    private sealed class IssueWebFixture : IAsyncDisposable
    {
        private const string Password = "M11-Issue-Password-2026!";
        private IssueWebFixture(WebApplication app, HttpClient client, string tempRoot)
        {
            App = app; Client = client; TempRoot = tempRoot;
        }
        private WebApplication App { get; }
        public HttpClient Client { get; }
        private string TempRoot { get; }

        public static async Task<IssueWebFixture> CreateAsync()
        {
            var tempRoot = TestDirectory.Create();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = GetWebProjectRoot(), EnvironmentName = Environments.Development });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "sqlite",
                ["ConnectionStrings:GitCandy"] = $"Data Source={Path.Combine(tempRoot, "GitCandy.db")};Pooling=False",
                ["GitCandy:Application:RepositoryPath"] = Path.Combine(tempRoot, "Repositories"),
                ["GitCandy:Application:CachePath"] = Path.Combine(tempRoot, "Caches"),
                ["GitCandy:Application:DataProtectionKeysPath"] = Path.Combine(tempRoot, "Keys"),
                ["GitCandy:Application:EnableSsh"] = "false"
            });
            builder.Services.AddGitCandyWebShell(builder.Configuration, builder.Environment.ContentRootPath);
            builder.Services.AddControllersWithViews().AddApplicationPart(typeof(IssueController).Assembly);
            builder.Services.RemoveAll<IHostedService>();
            builder.Services.ConfigureApplicationCookie(options => options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);
            var app = builder.Build();
            app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapGitCandyCompatibilityRoutes();
            try
            {
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
                    await dbContext.Database.MigrateAsync();
                    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
                    var user = new GitCandyUser { UserName = "issue-author", Email = "issue-author@example.com" };
                    var result = await userManager.CreateAsync(user, Password);
                    Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Errors.Select(error => error.Description)));
                    var repositoryNamespace = new GitCandyNamespace
                    {
                        OwnerType = NamespaceOwnerType.User,
                        UserId = user.Id,
                        Slug = "issue-author",
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    var repository = new GitCandyRepository
                    {
                        Namespace = repositoryNamespace,
                        Name = "issues",
                        StorageName = "issues",
                        Description = string.Empty,
                        CreatedAtUtc = DateTime.UtcNow,
                        AllowAnonymousRead = true,
                        WorkItemSequence = new GitCandyWorkItemSequence()
                    };
                    repository.UserRoles.Add(new GitCandyUserRepositoryRole { UserId = user.Id, AllowRead = true, AllowWrite = true, IsOwner = true });
                    dbContext.Repositories.Add(repository);
                    await dbContext.SaveChangesAsync();
                    dbContext.NamespaceClaims.Add(new GitCandyNamespaceClaim
                    {
                        NormalizedSlug = "ISSUE-AUTHOR",
                        Slug = "issue-author",
                        ClaimType = NameClaimType.Current,
                        NamespaceId = repositoryNamespace.Id
                    });
                    dbContext.RepositoryClaims.Add(new GitCandyRepositoryClaim
                    {
                        NamespaceId = repositoryNamespace.Id,
                        NormalizedSlug = "ISSUES",
                        Slug = "issues",
                        ClaimType = NameClaimType.Current,
                        RepositoryId = repository.Id
                    });
                    await dbContext.SaveChangesAsync();
                }
                await app.StartAsync();
                return new IssueWebFixture(app, CreateClient(app), tempRoot);
            }
            catch { await app.DisposeAsync(); TestDirectory.Delete(tempRoot); throw; }
        }

        public HttpClient CreateClient() => CreateClient(App);

        public string DescribeIssueRoutes() => string.Join(", ", App.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("issues", StringComparison.OrdinalIgnoreCase) == true)
            .Select(endpoint => endpoint.RoutePattern.RawText));

        public async Task LoginAsync()
        {
            var token = await GetAntiforgeryTokenAsync("/Account/Login");
            using var response = await Client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["UserNameOrEmail"] = "issue-author",
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

        public async Task MakeRepositoryPrivateAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            var repository = await db.Repositories.SingleAsync(item => item.Name == "issues");
            repository.IsPrivate = true; repository.AllowAnonymousRead = false;
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); await App.StopAsync(); await App.DisposeAsync(); TestDirectory.Delete(TempRoot);
        }

        private static HttpClient CreateClient(WebApplication app)
        {
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true }) { BaseAddress = new Uri(addresses.Single()) };
        }

        private static string GetWebProjectRoot()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GitCandy.slnx"))) directory = directory.Parent;
            directory ??= new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GitCandy.slnx"))) directory = directory.Parent;
            Assert.IsNotNull(directory);
            return Path.Combine(directory.FullName, "src", "GitCandy");
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenRegex();
}
