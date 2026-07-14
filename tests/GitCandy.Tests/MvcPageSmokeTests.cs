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

namespace GitCandy.Tests;

[TestClass]
public sealed class MvcPageSmokeTests
{
    private const string AdministratorPassword = "M5-Administrator-Password-2026!";

    [TestMethod]
    public async Task HomePage_WithAnonymousAndAuthenticatedUsers_RendersProductPageThenOpensWorkspace()
    {
        await using var fixture = await MvcWebFixture.CreateAsync();

        using var anonymousResponse = await fixture.Client.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.OK, anonymousResponse.StatusCode);
        var anonymousHtml = await anonymousResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(anonymousHtml, "class=\"landing-body\"");
        StringAssert.Contains(anonymousHtml, "id=\"collaboration\"");
        StringAssert.Contains(anonymousHtml, "/brand/gitcandy-mark.svg");
        StringAssert.Contains(anonymousHtml, "/Account/Login?returnUrl=%2F");

        using var logoResponse = await fixture.Client.GetAsync("/brand/gitcandy-mark.svg");
        Assert.AreEqual(HttpStatusCode.OK, logoResponse.StatusCode);
        Assert.AreEqual("image/svg+xml", logoResponse.Content.Headers.ContentType?.MediaType);

        await fixture.CreateAdministratorAsync();
        await fixture.LoginAsync("m5-admin", AdministratorPassword);

        using var authenticatedResponse = await fixture.Client.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.Redirect, authenticatedResponse.StatusCode);
        Assert.AreEqual("/me", authenticatedResponse.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task WorkspaceRoutes_WithAnonymousAuthenticatedAndPublicProfile_KeepPrivacyAndFixedRoutePriority()
    {
        await using var fixture = await MvcWebFixture.CreateAsync();
        await fixture.SeedRepositoriesAsync();

        foreach (var path in new[] { "/me", "/todos", "/notifications", "/me/repositories", "/me/settings", "/me/remotes" })
        {
            using var response = await fixture.Client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode, path);
            Assert.IsNotNull(response.Headers.Location);
            StringAssert.StartsWith(response.Headers.Location.AbsolutePath, "/Account/Login");
        }
        var explore = await fixture.GetStringAsync("/explore");
        StringAssert.Contains(explore, "public-repository");
        Assert.IsFalse(explore.Contains("private-repository", StringComparison.Ordinal));
        var search = await fixture.GetStringAsync("/search?q=repository");
        StringAssert.Contains(search, "profile-user/public-repository");
        Assert.IsFalse(search.Contains("private-repository", StringComparison.Ordinal));
        StringAssert.Contains(await fixture.GetStringAsync("/profile-user/public-repository/releases"), "No releases");

        var publicProfile = await fixture.GetStringAsync("/profile-user?tab=repositories");
        StringAssert.Contains(publicProfile, "Profile User");
        StringAssert.Contains(publicProfile, "public-repository");
        Assert.IsFalse(publicProfile.Contains("private-repository", StringComparison.Ordinal));
        Assert.IsFalse(publicProfile.Contains("profile-user@example.com", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(publicProfile.Contains("SSH keys", StringComparison.OrdinalIgnoreCase));
        using var invalidTab = await fixture.Client.GetAsync("/profile-user?tab=settings");
        Assert.AreEqual(HttpStatusCode.NotFound, invalidTab.StatusCode);

        await fixture.CreateAdministratorAsync();
        await fixture.LoginAsync("m5-admin", AdministratorPassword);
        var dashboard = await fixture.GetStringAsync("/me");
        StringAssert.Contains(dashboard, "Needs your attention");
        StringAssert.Contains(dashboard, "Activity feed");
        StringAssert.Contains(dashboard, "Public repositories");
        StringAssert.Contains(await fixture.GetStringAsync("/notifications/preferences"), "Notification delivery");
        var settings = await fixture.GetStringAsync("/me/settings");
        StringAssert.Contains(settings, "Remote accounts");
        var remotes = await fixture.GetStringAsync("/me/remotes");
        StringAssert.Contains(remotes, "GitHub");
        StringAssert.Contains(remotes, "GitLab");
        StringAssert.Contains(remotes, "Gitee");
        StringAssert.Contains(remotes, "name=\"Form.Secret\"");
        StringAssert.Contains(remotes, "value=\"\"");
        var administratorView = await fixture.GetStringAsync("/profile-user?tab=repositories");
        Assert.IsFalse(administratorView.Contains("private-repository", StringComparison.Ordinal));
        Assert.IsFalse(administratorView.Contains("profile-user@example.com", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task RepositoryPages_WithAnonymousUser_ShowOnlyPublicRepositoriesAndLocalizedAssets()
    {
        await using var fixture = await MvcWebFixture.CreateAsync();
        await fixture.SeedRepositoriesAsync();

        using var response = await fixture.Client.GetAsync("/Repository");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.Contains("X-GitCandy-Version"));
        var html = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(html, "public-repository");
        Assert.IsFalse(html.Contains("private-repository", StringComparison.Ordinal));
        StringAssert.Contains(html, "/assets/app.css");
        StringAssert.Contains(html, "/assets/app.js");
        StringAssert.Contains(html, "data-theme=\"system\"");
        StringAssert.Contains(html, "class=\"app-sidebar\"");
        StringAssert.Contains(html, "class=\"theme-control\"");
        Assert.IsFalse(html.Contains("bootstrap.css", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("jquery", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(html, "/Account/Login?returnUrl=");

        var accountHtml = await fixture.GetStringAsync("/Account/Detail/profile-user");
        StringAssert.Contains(accountHtml, "public-repository");
        Assert.IsFalse(accountHtml.Contains("private-repository", StringComparison.Ordinal));
        var teamHtml = await fixture.GetStringAsync("/Team/Detail/profile-team");
        StringAssert.Contains(teamHtml, "public-repository");
        Assert.IsFalse(teamHtml.Contains("private-repository", StringComparison.Ordinal));

        string[] expectedAssets =
        [
            "/assets/app.css",
            "/assets/app.js"
        ];
        foreach (var assetPath in expectedAssets)
        {
            using var staticAsset = await fixture.Client.GetAsync(assetPath);
            Assert.AreEqual(HttpStatusCode.OK, staticAsset.StatusCode, assetPath);
        }

        using var languageResponse = await fixture.Client.GetAsync(
            "/Home/Language?lang=zh-cn&returnUrl=%2FRepository");
        Assert.AreEqual(HttpStatusCode.Redirect, languageResponse.StatusCode);
        Assert.IsTrue(languageResponse.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.IsTrue(cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal)));
        Assert.IsTrue(cookies.Any(cookie => cookie.StartsWith("Lang=", StringComparison.Ordinal)));

        var localizedHtml = WebUtility.HtmlDecode(await fixture.GetStringAsync("/Repository"));
        StringAssert.Contains(localizedHtml, "代码库列表");
    }

    [TestMethod]
    public async Task ApplicationShell_WithThemeCookie_RendersThemeBeforeClientScriptRuns()
    {
        await using var fixture = await MvcWebFixture.CreateAsync();
        fixture.Client.DefaultRequestHeaders.Add("Cookie", ".GitCandy.Theme=dark");

        var html = await fixture.GetStringAsync("/Repository");

        StringAssert.Contains(html, "<html lang=\"en-US\" data-theme=\"dark\">");
        StringAssert.Contains(html, "aria-pressed=\"true\" data-theme-value=\"dark\"");
    }

    [TestMethod]
    public async Task CrudPages_WithAdministrator_SubmitValidatedAntiforgeryProtectedForms()
    {
        await using var fixture = await MvcWebFixture.CreateAsync();
        await fixture.CreateAdministratorAsync();
        await fixture.LoginAsync("m5-admin", AdministratorPassword);

        StringAssert.Contains(await fixture.GetStringAsync("/Account/Index"), "m5-admin");
        StringAssert.Contains(await fixture.GetStringAsync("/Setting/Edit"), "Effective runtime configuration");

        var invalidRepositoryToken = await fixture.GetAntiforgeryTokenAsync("/Repository/Create");
        using var invalidRepositoryResponse = await fixture.PostFormAsync(
            "/Repository/Create",
            invalidRepositoryToken,
            new Dictionary<string, string>
            {
                ["Name"] = "x",
                ["Description"] = "invalid repository"
            });
        Assert.AreEqual(HttpStatusCode.OK, invalidRepositoryResponse.StatusCode);
        StringAssert.Contains(
            await invalidRepositoryResponse.Content.ReadAsStringAsync(),
            "minimum length");

        var repositoryToken = await fixture.GetAntiforgeryTokenAsync("/Repository/Create");
        using var repositoryResponse = await fixture.PostFormAsync(
            "/Repository/Create",
            repositoryToken,
            new Dictionary<string, string>
            {
                ["Name"] = "m5-repository",
                ["Description"] = "M5 repository",
                ["IsPrivate"] = "false",
                ["AllowAnonymousRead"] = "true",
                ["AllowAnonymousWrite"] = "false"
            });
        Assert.AreEqual(HttpStatusCode.Redirect, repositoryResponse.StatusCode);
        Assert.AreEqual(
            "/m5-admin/m5-repository",
            repositoryResponse.Headers.Location?.OriginalString);

        var repositoryHtml = await fixture.GetStringAsync("/m5-admin/m5-repository");
        StringAssert.Contains(repositoryHtml, "M5 repository");
        StringAssert.Contains(
            repositoryHtml,
            $"{fixture.Client.BaseAddress}m5-admin/m5-repository.git");
        StringAssert.Contains(repositoryHtml, "href=\"/m5-admin/m5-repository/issues\"");
        StringAssert.Contains(repositoryHtml, "href=\"/m5-admin/m5-repository/settings/deploy-keys\"");
        StringAssert.Contains(repositoryHtml, "href=\"/m5-admin/m5-repository/settings/branch-rules\"");
        StringAssert.Contains(repositoryHtml, "href=\"/m5-admin/m5-repository/settings/webhooks\"");
        StringAssert.Contains(repositoryHtml, "href=\"/m5-admin/m5-repository/releases\"");
        Assert.IsFalse(repositoryHtml.Contains("/git/m5-repository.git", StringComparison.Ordinal));

        var deployKeysHtml = await fixture.GetStringAsync(
            "/m5-admin/m5-repository/settings/deploy-keys");
        StringAssert.Contains(deployKeysHtml, "Add deploy key");
        StringAssert.Contains(
            deployKeysHtml,
            "action=\"/m5-admin/m5-repository/settings/deploy-keys\"");
        StringAssert.Contains(deployKeysHtml, "name=\"__RequestVerificationToken\"");

        const string branchRulesPath = "/m5-admin/m5-repository/settings/branch-rules";
        var branchRulesHtml = await fixture.GetStringAsync(branchRulesPath);
        StringAssert.Contains(branchRulesHtml, "Add rule");
        StringAssert.Contains(branchRulesHtml, "name=\"__RequestVerificationToken\"");
        var branchRuleToken = await fixture.GetAntiforgeryTokenAsync(branchRulesPath);
        using var branchRuleResponse = await fixture.PostFormAsync(
            branchRulesPath,
            branchRuleToken,
            new Dictionary<string, string>
            {
                ["Rule.RepositoryName"] = "m5-repository",
                ["Rule.Pattern"] = "main",
                ["Rule.PushAccess"] = "1",
                ["Rule.MergeAccess"] = "2",
                ["Rule.RequiredChecks"] = "ci/build, security/scan",
                ["Rule.RequiredApprovals"] = "2",
                ["Rule.RequireCodeOwnerReviews"] = "true",
                ["Rule.DismissStaleApprovals"] = "true",
                ["Rule.AllowAdministratorBypass"] = "true"
            });
        Assert.AreEqual(HttpStatusCode.Redirect, branchRuleResponse.StatusCode);
        Assert.AreEqual(branchRulesPath, branchRuleResponse.Headers.Location?.OriginalString);
        branchRulesHtml = await fixture.GetStringAsync(branchRulesPath);
        StringAssert.Contains(branchRulesHtml, "<code>main</code>");
        StringAssert.Contains(branchRulesHtml, "RepositoryOwner");
        StringAssert.Contains(branchRulesHtml, "Nobody");
        StringAssert.Contains(branchRulesHtml, "ci/build, security/scan");
        StringAssert.Contains(branchRulesHtml, "2 approval(s) / Code owners / Fresh head");

        const string webhooksPath = "/m5-admin/m5-repository/settings/webhooks";
        var webhooksHtml = await fixture.GetStringAsync(webhooksPath);
        StringAssert.Contains(webhooksHtml, "Add webhook");
        StringAssert.Contains(webhooksHtml, $"action=\"{webhooksPath}\"");
        StringAssert.Contains(webhooksHtml, "name=\"__RequestVerificationToken\"");
        var webhookToken = await fixture.GetAntiforgeryTokenAsync(webhooksPath);
        using var webhookResponse = await fixture.PostFormAsync(
            webhooksPath,
            webhookToken,
            new Dictionary<string, string>
            {
                ["Create.Name"] = "external-ci",
                ["Create.TargetUrl"] = "https://8.8.8.8/hooks/gitcandy",
                ["Create.Push"] = "true",
                ["Create.PullRequestMerged"] = "true"
            });
        Assert.AreEqual(HttpStatusCode.OK, webhookResponse.StatusCode);
        var webhookCreatedHtml = await webhookResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(webhookCreatedHtml, "This signing secret is shown once");
        StringAssert.Contains(webhookCreatedHtml, "whsec_");
        webhooksHtml = await fixture.GetStringAsync(webhooksPath);
        StringAssert.Contains(webhooksHtml, "external-ci");
        Assert.IsFalse(webhooksHtml.Contains("whsec_", StringComparison.Ordinal));
        StringAssert.Contains(
            await fixture.GetStringAsync("/m5-admin/m5-repository/settings/audit"),
            "rule.save");
        StringAssert.Contains(
            await fixture.GetStringAsync("/m5-admin/m5-repository/releases/new"),
            "New release");

        using var repositoryListResponse = await fixture.Client.GetAsync("/Repository");
        Assert.AreEqual(HttpStatusCode.OK, repositoryListResponse.StatusCode);
        StringAssert.Contains(
            await repositoryListResponse.Content.ReadAsStringAsync(),
            "href=\"/m5-admin/m5-repository\"");

        using var legacyDetailResponse = await fixture.Client.GetAsync("/Repository/Detail/m5-repository");
        Assert.AreEqual(HttpStatusCode.NotFound, legacyDetailResponse.StatusCode);
        using var legacyTreeResponse = await fixture.Client.GetAsync("/Repository/Tree/m5-repository");
        Assert.AreEqual(HttpStatusCode.NotFound, legacyTreeResponse.StatusCode);
        StringAssert.Contains(
            await fixture.GetStringAsync("/m5-admin/m5-repository/tree"),
            "No commits are available.");
        Assert.IsTrue(fixture.PhysicalRepositoryExists("m5-repository"));

        var repositoryEditToken = await fixture.GetAntiforgeryTokenAsync("/Repository/Edit/m5-repository");
        using var repositoryEditResponse = await fixture.PostFormAsync(
            "/Repository/Edit/m5-repository",
            repositoryEditToken,
            new Dictionary<string, string>
            {
                ["Name"] = "m5-repository",
                ["Description"] = "M5 repository updated",
                ["IsPrivate"] = "true",
                ["AllowAnonymousRead"] = "true",
                ["AllowAnonymousWrite"] = "true"
            });
        Assert.AreEqual(HttpStatusCode.Redirect, repositoryEditResponse.StatusCode);
        var repository = await fixture.FindRepositoryAsync("m5-repository");
        Assert.IsNotNull(repository);
        Assert.IsTrue(repository.IsPrivate);
        Assert.IsFalse(repository.AllowAnonymousRead);
        Assert.IsFalse(repository.AllowAnonymousWrite);

        var teamToken = await fixture.GetAntiforgeryTokenAsync("/Team/Create");
        using var teamResponse = await fixture.PostFormAsync(
            "/Team/Create",
            teamToken,
            new Dictionary<string, string>
            {
                ["Name"] = "m5-team",
                ["Description"] = "M5 team"
            });
        Assert.AreEqual(HttpStatusCode.Redirect, teamResponse.StatusCode);
        StringAssert.Contains(await fixture.GetStringAsync("/Team/Detail/m5-team"), "M5 team");

        var repositoryDeleteToken = await fixture.GetAntiforgeryTokenAsync("/Repository/Delete/m5-repository");
        using var repositoryDeleteResponse = await fixture.PostFormAsync(
            "/Repository/Delete/m5-repository",
            repositoryDeleteToken,
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.Redirect, repositoryDeleteResponse.StatusCode);
        Assert.IsNull(await fixture.FindRepositoryAsync("m5-repository"));
        Assert.IsFalse(fixture.PhysicalRepositoryExists("m5-repository"));

        var teamDeleteToken = await fixture.GetAntiforgeryTokenAsync("/Team/Delete/m5-team");
        using var teamDeleteResponse = await fixture.PostFormAsync(
            "/Team/Delete/m5-team",
            teamDeleteToken,
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.Redirect, teamDeleteResponse.StatusCode);
    }

    private sealed class MvcWebFixture : IAsyncDisposable
    {
        private static readonly Regex AntiforgeryTokenPattern = new(
            "<input name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        private MvcWebFixture(WebApplication app, HttpClient client, string tempRoot)
        {
            App = app;
            Client = client;
            TempRoot = tempRoot;
        }

        private WebApplication App { get; }

        public HttpClient Client { get; }

        private string TempRoot { get; }

        public bool PhysicalRepositoryExists(string repositoryName)
        {
            return Directory.Exists(Path.Combine(TempRoot, "Repositories", repositoryName))
                || Directory.Exists(Path.Combine(TempRoot, "Repositories", $"{repositoryName}.git"));
        }

        public static async Task<MvcWebFixture> CreateAsync()
        {
            var tempRoot = TestDirectory.Create();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = GetWebProjectRoot(),
                WebRootPath = "wwwroot",
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseKestrel();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "sqlite",
                ["ConnectionStrings:GitCandy"] = $"Data Source={Path.Combine(tempRoot, "GitCandy.db")};Pooling=False",
                ["GitCandy:Application:RepositoryPath"] = Path.Combine(tempRoot, "Repositories"),
                ["GitCandy:Application:CachePath"] = Path.Combine(tempRoot, "Caches"),
                ["GitCandy:Application:EnableSsh"] = "false",
                ["GitCandy:Application:AllowRegisterUser"] = "true",
                ["GitCandy:Application:AllowRepositoryCreation"] = "true"
            });
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AccountController).Assembly);
            builder.Services.RemoveAll<IHostedService>();
            builder.Services.ConfigureApplicationCookie(options =>
            {
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });

            var app = builder.Build();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseRequestLocalization();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapGitCandyCompatibilityRoutes();

            try
            {
                await using (var scope = app.Services.CreateAsyncScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
                    await dbContext.Database.EnsureCreatedAsync();
                }

                await app.StartAsync();
                var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true };
                var client = new HttpClient(handler) { BaseAddress = new Uri(GetServerAddress(app)) };
                return new MvcWebFixture(app, client, tempRoot);
            }
            catch
            {
                await app.DisposeAsync();
                TestDirectory.Delete(tempRoot);
                throw;
            }
        }

        public async Task SeedRepositoriesAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
            var user = new GitCandyUser
            {
                UserName = "profile-user",
                Email = "profile-user@example.com",
                DisplayName = "Profile User"
            };
            Assert.IsTrue((await userManager.CreateAsync(user)).Succeeded);

            var userNamespace = new GitCandyNamespace
            {
                OwnerType = NamespaceOwnerType.User,
                UserId = user.Id,
                Slug = "profile-user",
                NormalizedSlug = "PROFILE-USER",
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.Namespaces.Add(userNamespace);
            await dbContext.SaveChangesAsync();
            dbContext.NamespaceClaims.Add(new GitCandyNamespaceClaim
            {
                NamespaceId = userNamespace.Id,
                Slug = userNamespace.Slug,
                NormalizedSlug = userNamespace.NormalizedSlug,
                ClaimType = NameClaimType.Current
            });

            var publicRepository = new GitCandyRepository
            {
                NamespaceId = userNamespace.Id,
                Name = "public-repository",
                Description = "Public repository",
                CreatedAtUtc = DateTime.UtcNow,
                AllowAnonymousRead = true
            };
            var privateRepository = new GitCandyRepository
            {
                NamespaceId = userNamespace.Id,
                Name = "private-repository",
                Description = "Private repository",
                CreatedAtUtc = DateTime.UtcNow,
                IsPrivate = true
            };
            publicRepository.UserRoles.Add(new GitCandyUserRepositoryRole
            {
                UserId = user.Id,
                AllowRead = true,
                IsOwner = true
            });
            privateRepository.UserRoles.Add(new GitCandyUserRepositoryRole
            {
                UserId = user.Id,
                AllowRead = true,
                IsOwner = true
            });
            var team = new GitCandyTeam
            {
                Name = "profile-team",
                Description = "Profile team",
                CreatedAtUtc = DateTime.UtcNow
            };
            team.UserRoles.Add(new GitCandyUserTeamRole { UserId = user.Id });
            team.RepositoryRoles.Add(new GitCandyTeamRepositoryRole
            {
                Repository = publicRepository,
                AllowRead = true
            });
            team.RepositoryRoles.Add(new GitCandyTeamRepositoryRole
            {
                Repository = privateRepository,
                AllowRead = true
            });
            dbContext.Repositories.AddRange(publicRepository, privateRepository);
            dbContext.Teams.Add(team);
            await dbContext.SaveChangesAsync();
            dbContext.RepositoryClaims.AddRange(
                new GitCandyRepositoryClaim { NamespaceId = userNamespace.Id, RepositoryId = publicRepository.Id,
                    Slug = publicRepository.Name, NormalizedSlug = publicRepository.NormalizedName, ClaimType = NameClaimType.Current },
                new GitCandyRepositoryClaim { NamespaceId = userNamespace.Id, RepositoryId = privateRepository.Id,
                    Slug = privateRepository.Name, NormalizedSlug = privateRepository.NormalizedName, ClaimType = NameClaimType.Current });
            await dbContext.SaveChangesAsync();
        }

        public async Task CreateAdministratorAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
            Assert.IsTrue((await roleManager.CreateAsync(new IdentityRole(RoleNames.Administrator))).Succeeded);
            var user = new GitCandyUser
            {
                UserName = "m5-admin",
                Email = "m5-admin@example.com",
                DisplayName = "M5 Administrator"
            };
            Assert.IsTrue((await userManager.CreateAsync(user, AdministratorPassword)).Succeeded);
            Assert.IsTrue((await userManager.AddToRoleAsync(user, RoleNames.Administrator)).Succeeded);
        }

        public async Task LoginAsync(string userName, string password)
        {
            var token = await GetAntiforgeryTokenAsync("/Account/Login");
            using var response = await PostFormAsync(
                "/Account/Login",
                token,
                new Dictionary<string, string>
                {
                    ["UserNameOrEmail"] = userName,
                    ["Password"] = password,
                    ["RememberMe"] = "false"
                });
            Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        }

        public async Task<string> GetAntiforgeryTokenAsync(string path)
        {
            var html = await GetStringAsync(path);
            var match = AntiforgeryTokenPattern.Match(html);
            Assert.IsTrue(match.Success, $"No antiforgery token was rendered for '{path}'.");
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }

        public async Task<string> GetStringAsync(string path)
        {
            using var response = await Client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"GET {path} failed.");
            return await response.Content.ReadAsStringAsync();
        }

        public Task<HttpResponseMessage> PostFormAsync(
            string path,
            string antiforgeryToken,
            IReadOnlyDictionary<string, string> fields)
        {
            var formFields = fields.ToDictionary(static pair => pair.Key, static pair => pair.Value);
            formFields["__RequestVerificationToken"] = antiforgeryToken;
            return Client.PostAsync(path, new FormUrlEncodedContent(formFields));
        }

        public async Task<GitCandyRepository?> FindRepositoryAsync(string name)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            var normalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(name);
            return await dbContext.Repositories.AsNoTracking().SingleOrDefaultAsync(
                repository => repository.NormalizedName == normalizedName);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            TestDirectory.Delete(TempRoot);
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
}
