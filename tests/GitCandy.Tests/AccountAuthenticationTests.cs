using System.Net;
using System.Text.RegularExpressions;
using GitCandy.Configuration;
using GitCandy.Controllers;
using GitCandy.Data;
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
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class AccountAuthenticationTests
{
    private const string OriginalPassword = "M4-Original-Password-2026!";
    private const string ChangedPassword = "M4-Changed-Password-2026!";
    private static readonly Regex AntiforgeryTokenPattern = new(
        "<input name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
        RegexOptions.CultureInvariant);

    [TestMethod]
    public async Task AccountFlow_WithRegistrationLoginLogoutAndPasswordChange_UsesIdentityCookie()
    {
        await using var fixture = await AccountWebFixture.CreateAsync();

        var createToken = await fixture.GetAntiforgeryTokenAsync("/Account/Create");
        using var createResponse = await fixture.PostFormAsync(
            "/Account/Create",
            createToken,
            new Dictionary<string, string>
            {
                ["UserName"] = "account-user",
                ["DisplayName"] = "Account User",
                ["Email"] = "account-user@example.com",
                ["Password"] = OriginalPassword,
                ["ConfirmPassword"] = OriginalPassword,
                ["Description"] = "M4 account flow"
            });

        Assert.AreEqual(HttpStatusCode.Redirect, createResponse.StatusCode);
        Assert.IsTrue(createResponse.Headers.TryGetValues("Set-Cookie", out var createCookies));
        Assert.IsTrue(createCookies.Any(cookie => cookie.StartsWith(".GitCandy.Identity=", StringComparison.Ordinal)));
        Assert.IsFalse(createCookies.Any(cookie => cookie.StartsWith(".GitCandy.Session=", StringComparison.Ordinal)));

        var createdUser = await fixture.FindUserAsync("account-user");
        Assert.IsNotNull(createdUser);
        Assert.AreEqual("Account User", createdUser.DisplayName);
        Assert.IsTrue(await fixture.CheckPasswordAsync(createdUser, OriginalPassword));

        var authenticatedHome = await fixture.GetStringAsync("/Repository");
        StringAssert.Contains(authenticatedHome, "account-user");

        var changeToken = await fixture.GetAntiforgeryTokenAsync("/Account/Change");
        using var changeResponse = await fixture.PostFormAsync(
            "/Account/Change",
            changeToken,
            new Dictionary<string, string>
            {
                ["CurrentPassword"] = OriginalPassword,
                ["NewPassword"] = ChangedPassword,
                ["ConfirmPassword"] = ChangedPassword
            });
        Assert.AreEqual(HttpStatusCode.Redirect, changeResponse.StatusCode);

        var changedPage = await fixture.GetStringAsync("/Account/Change?changed=true");
        StringAssert.Contains(changedPage, "Password changed.");

        var logoutToken = await fixture.GetAntiforgeryTokenAsync("/Repository");
        using var logoutResponse = await fixture.PostFormAsync(
            "/Account/Logout",
            logoutToken,
            new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.Redirect, logoutResponse.StatusCode);

        var loginToken = await fixture.GetAntiforgeryTokenAsync("/Account/Login");
        using var oldPasswordResponse = await fixture.PostFormAsync(
            "/Account/Login",
            loginToken,
            LoginForm("account-user@example.com", OriginalPassword));
        Assert.AreEqual(HttpStatusCode.OK, oldPasswordResponse.StatusCode);
        StringAssert.Contains(
            await oldPasswordResponse.Content.ReadAsStringAsync(),
            "Unable to sign in with the supplied credentials.");

        var newLoginToken = await fixture.GetAntiforgeryTokenAsync("/Account/Login");
        using var newPasswordResponse = await fixture.PostFormAsync(
            "/Account/Login",
            newLoginToken,
            LoginForm("account-user@example.com", ChangedPassword));
        Assert.AreEqual(HttpStatusCode.Redirect, newPasswordResponse.StatusCode);

        var updatedUser = await fixture.FindUserAsync("account-user");
        Assert.IsNotNull(updatedUser);
        Assert.IsFalse(await fixture.CheckPasswordAsync(updatedUser, OriginalPassword));
        Assert.IsTrue(await fixture.CheckPasswordAsync(updatedUser, ChangedPassword));
    }

    [TestMethod]
    public async Task IdentityCookie_AfterSecurityStampChanges_IsRejectedOnNextValidation()
    {
        await using var fixture = await AccountWebFixture.CreateAsync();
        await fixture.CreateUserAsync("stamp-user", "stamp-user@example.com", OriginalPassword);
        await fixture.LoginAsync("stamp-user", OriginalPassword);

        StringAssert.Contains(await fixture.GetStringAsync("/Repository"), "stamp-user");

        await fixture.UpdateSecurityStampAsync("stamp-user");
        var homeAfterStampChange = await fixture.GetStringAsync("/Repository");

        StringAssert.Contains(homeAfterStampChange, "Login");
        Assert.IsFalse(homeAfterStampChange.Contains(">stamp-user<", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task IdentityCookie_AfterPasswordChangesInAnotherSession_IsRejected()
    {
        await using var fixture = await AccountWebFixture.CreateAsync();
        await fixture.CreateUserAsync("multi-session-user", "multi-session@example.com", OriginalPassword);
        using var secondClient = fixture.CreateClient();

        await fixture.LoginAsync("multi-session-user", OriginalPassword);
        await fixture.LoginAsync("multi-session-user", OriginalPassword, secondClient);

        var changeToken = await fixture.GetAntiforgeryTokenAsync("/Account/Change");
        using var changeResponse = await fixture.PostFormAsync(
            "/Account/Change",
            changeToken,
            new Dictionary<string, string>
            {
                ["CurrentPassword"] = OriginalPassword,
                ["NewPassword"] = ChangedPassword,
                ["ConfirmPassword"] = ChangedPassword
            });
        Assert.AreEqual(HttpStatusCode.Redirect, changeResponse.StatusCode);

        var secondSessionHome = await fixture.GetStringAsync("/Repository", secondClient);
        StringAssert.Contains(secondSessionHome, "Login");
        Assert.IsFalse(secondSessionHome.Contains(">multi-session-user<", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Login_WithRepeatedInvalidPasswords_LocksUserAndRejectsValidPassword()
    {
        await using var fixture = await AccountWebFixture.CreateAsync();
        await fixture.CreateUserAsync("locked-user", "locked-user@example.com", OriginalPassword);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var token = await fixture.GetAntiforgeryTokenAsync("/Account/Login");
            using var response = await fixture.PostFormAsync(
                "/Account/Login",
                token,
                LoginForm("locked-user", "incorrect-password"));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        var lockedUser = await fixture.FindUserAsync("locked-user");
        Assert.IsNotNull(lockedUser);
        Assert.IsNotNull(lockedUser.LockoutEnd);
        Assert.IsTrue(lockedUser.LockoutEnd > DateTimeOffset.UtcNow);

        var validPasswordToken = await fixture.GetAntiforgeryTokenAsync("/Account/Login");
        using var validPasswordResponse = await fixture.PostFormAsync(
            "/Account/Login",
            validPasswordToken,
            LoginForm("locked-user", OriginalPassword));

        Assert.AreEqual(HttpStatusCode.OK, validPasswordResponse.StatusCode);
        StringAssert.Contains(
            await validPasswordResponse.Content.ReadAsStringAsync(),
            "Unable to sign in with the supplied credentials.");
    }

    private static Dictionary<string, string> LoginForm(string userNameOrEmail, string password)
    {
        return new Dictionary<string, string>
        {
            ["UserNameOrEmail"] = userNameOrEmail,
            ["Password"] = password,
            ["RememberMe"] = "false"
        };
    }

    private sealed class AccountWebFixture : IAsyncDisposable
    {
        private AccountWebFixture(WebApplication app, HttpClient client, string tempRoot)
        {
            App = app;
            Client = client;
            TempRoot = tempRoot;
        }

        private WebApplication App { get; }

        private HttpClient Client { get; }

        private string TempRoot { get; }

        public static async Task<AccountWebFixture> CreateAsync()
        {
            var tempRoot = TestDirectory.Create();
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
                ["GitCandy:Application:RepositoryPath"] = Path.Combine(tempRoot, "Repositories"),
                ["GitCandy:Application:CachePath"] = Path.Combine(tempRoot, "Caches"),
                ["GitCandy:Application:EnableSsh"] = "false",
                ["GitCandy:Application:AllowRegisterUser"] = "true"
            });
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            builder.Services.AddControllersWithViews()
                .AddApplicationPart(typeof(AccountController).Assembly);
            builder.Services.RemoveAll<IHostedService>();
            builder.Services.ConfigureApplicationCookie(options =>
            {
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });
            builder.Services.Configure<SecurityStampValidatorOptions>(options =>
            {
                options.ValidationInterval = TimeSpan.Zero;
            });

            var app = builder.Build();
            Assert.IsTrue(app.Services
                .GetRequiredService<IOptions<GitCandyApplicationOptions>>()
                .Value
                .AllowRegisterUser);
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
                var client = CreateClient(app);

                return new AccountWebFixture(app, client, tempRoot);
            }
            catch
            {
                await app.DisposeAsync();
                TestDirectory.Delete(tempRoot);
                throw;
            }
        }

        public HttpClient CreateClient()
        {
            return CreateClient(App);
        }

        public async Task<string> GetAntiforgeryTokenAsync(string path, HttpClient? client = null)
        {
            var html = await GetStringAsync(path, client);
            var match = AntiforgeryTokenPattern.Match(html);
            Assert.IsTrue(match.Success, $"No antiforgery token was rendered for '{path}'.");
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }

        public async Task<string> GetStringAsync(string path, HttpClient? client = null)
        {
            using var response = await (client ?? Client).GetAsync(path);
            Assert.AreEqual(
                HttpStatusCode.OK,
                response.StatusCode,
                $"GET {path} failed.");
            return await response.Content.ReadAsStringAsync();
        }

        public Task<HttpResponseMessage> PostFormAsync(
            string path,
            string antiforgeryToken,
            IReadOnlyDictionary<string, string> fields,
            HttpClient? client = null)
        {
            var formFields = fields.ToDictionary(static pair => pair.Key, static pair => pair.Value);
            formFields["__RequestVerificationToken"] = antiforgeryToken;
            return (client ?? Client).PostAsync(path, new FormUrlEncodedContent(formFields));
        }

        public async Task CreateUserAsync(string userName, string email, string password)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
            var result = await userManager.CreateAsync(
                new GitCandyUser
                {
                    UserName = userName,
                    Email = email
                },
                password);
            Assert.IsTrue(
                result.Succeeded,
                string.Join(Environment.NewLine, result.Errors.Select(static error => error.Description)));
        }

        public async Task LoginAsync(
            string userNameOrEmail,
            string password,
            HttpClient? client = null)
        {
            var token = await GetAntiforgeryTokenAsync("/Account/Login", client);
            using var response = await PostFormAsync(
                "/Account/Login",
                token,
                LoginForm(userNameOrEmail, password),
                client);
            Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        }

        public async Task<GitCandyUser?> FindUserAsync(string userName)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
            return await userManager.FindByNameAsync(userName);
        }

        public async Task<bool> CheckPasswordAsync(GitCandyUser user, string password)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
            var currentUser = await userManager.FindByIdAsync(user.Id);
            Assert.IsNotNull(currentUser);
            return await userManager.CheckPasswordAsync(currentUser, password);
        }

        public async Task UpdateSecurityStampAsync(string userName)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
            var user = await userManager.FindByNameAsync(userName);
            Assert.IsNotNull(user);
            var result = await userManager.UpdateSecurityStampAsync(user);
            Assert.IsTrue(result.Succeeded);
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
            Assert.AreEqual(1, addresses.Count);
            return addresses.Single();
        }

        private static HttpClient CreateClient(WebApplication app)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true
            };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri(GetServerAddress(app))
            };
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
