using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using GitCandy.Authentication;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Identity;
using GitCandy.Credentials;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace GitCandy.Tests;

[TestClass]
public sealed class GitBasicAuthenticationHandlerTests
{
    private const string Password = "M4-Valid-Password-2026!";

    [TestMethod]
    public async Task AuthenticateAsync_WithValidIdentityCredentials_ReturnsGitBasicPrincipal()
    {
        await using var fixture = await BasicAuthenticationFixture.CreateAsync();

        var result = await fixture.AuthenticateAsync("git-user@example.com", Password);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(GitCandyAuthenticationSchemes.GitBasic, result.Ticket?.AuthenticationScheme);
        Assert.AreEqual(
            "git-user",
            result.Principal?.FindFirstValue(ClaimTypes.Name));
        Assert.AreEqual(
            BasicAuthenticationFixture.UserId,
            result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [TestMethod]
    public async Task AuthenticateAsync_WithGitScopedPersonalAccessToken_ReturnsMachineCredentialClaims()
    {
        await using var fixture = await BasicAuthenticationFixture.CreateAsync();
        var token = await fixture.CreateTokenAsync(PersonalAccessTokenScopes.GitRead);

        var result = await fixture.AuthenticateAsync("git-user", token.Token);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(
            CredentialClaimTypes.PersonalAccessToken,
            result.Principal?.FindFirstValue(CredentialClaimTypes.CredentialKind));
        Assert.IsTrue(result.Principal?.HasClaim(
            CredentialClaimTypes.Scope,
            PersonalAccessTokenScopes.GitRead));
        Assert.AreEqual(token.Summary.Id.ToString(), result.Principal?.FindFirstValue(CredentialClaimTypes.CredentialId));
    }

    [TestMethod]
    public async Task AuthenticateAsync_WithPersonalAccessTokenForDifferentUser_ReturnsFailure()
    {
        await using var fixture = await BasicAuthenticationFixture.CreateAsync();
        var token = await fixture.CreateTokenAsync(PersonalAccessTokenScopes.GitRead);

        var result = await fixture.AuthenticateAsync("someone-else", token.Token);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, (await fixture.GetLockoutStateAsync()).AccessFailedCount);
    }

    [TestMethod]
    public async Task PersonalAccessTokenScheme_WithApiScope_AuthenticatesBearerWithoutCookie()
    {
        await using var fixture = await BasicAuthenticationFixture.CreateAsync();
        var token = await fixture.CreateTokenAsync(PersonalAccessTokenScopes.ApiRead);

        var result = await fixture.AuthenticateBearerAsync(token.Token);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(GitCandyAuthenticationSchemes.PersonalAccessToken, result.Ticket?.AuthenticationScheme);
        Assert.IsTrue(result.Principal?.HasClaim(CredentialClaimTypes.Scope, PersonalAccessTokenScopes.ApiRead));
    }

    [TestMethod]
    public async Task AuthenticateAsync_WithFailedThenValidPassword_TracksAndResetsFailureCount()
    {
        await using var fixture = await BasicAuthenticationFixture.CreateAsync();

        var failedResult = await fixture.AuthenticateAsync("git-user", "incorrect-password");
        var failedState = await fixture.GetLockoutStateAsync();
        var succeededResult = await fixture.AuthenticateAsync("git-user", Password);
        var succeededState = await fixture.GetLockoutStateAsync();

        Assert.IsFalse(failedResult.Succeeded);
        Assert.AreEqual(1, failedState.AccessFailedCount);
        Assert.IsTrue(succeededResult.Succeeded);
        Assert.AreEqual(0, succeededState.AccessFailedCount);
    }

    [TestMethod]
    public async Task AuthenticateAsync_WithRepeatedInvalidPasswords_LocksIdentityUser()
    {
        await using var fixture = await BasicAuthenticationFixture.CreateAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var result = await fixture.AuthenticateAsync("git-user", "incorrect-password");
            Assert.IsFalse(result.Succeeded);
        }

        var lockoutState = await fixture.GetLockoutStateAsync();
        var validPasswordResult = await fixture.AuthenticateAsync("git-user", Password);

        Assert.IsNotNull(lockoutState.LockoutEnd);
        Assert.IsTrue(lockoutState.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.IsFalse(validPasswordResult.Succeeded);
    }

    [TestMethod]
    public async Task ChallengeAsync_WithoutCredentials_ReturnsBasicChallengeWithoutSession()
    {
        await using var fixture = await BasicAuthenticationFixture.CreateAsync();

        var challenge = await fixture.ChallengeAsync();

        Assert.AreEqual(StatusCodes.Status401Unauthorized, challenge.StatusCode);
        Assert.AreEqual(
            "Basic realm=\"GitCandy\", charset=\"UTF-8\"",
            challenge.WwwAuthenticate);
    }

    private sealed class BasicAuthenticationFixture : IAsyncDisposable
    {
        public const string UserId = "user-git-basic";

        private BasicAuthenticationFixture(ServiceProvider serviceProvider, string tempRoot)
        {
            ServiceProvider = serviceProvider;
            TempRoot = tempRoot;
        }

        private ServiceProvider ServiceProvider { get; }

        private string TempRoot { get; }

        public static async Task<BasicAuthenticationFixture> CreateAsync()
        {
            var tempRoot = TestDirectory.Create();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={Path.Combine(tempRoot, "GitCandy.db")};Pooling=False"
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGitCandyWebShell(configuration);
            Assert.IsFalse(services.Any(service =>
                string.Equals(
                    service.ServiceType.FullName,
                    "Microsoft.AspNetCore.Session.ISessionStore",
                    StringComparison.Ordinal)));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var fixture = new BasicAuthenticationFixture(serviceProvider, tempRoot);

            try
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
                await dbContext.Database.EnsureCreatedAsync();

                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
                var result = await userManager.CreateAsync(
                    new GitCandyUser
                    {
                        Id = UserId,
                        UserName = "git-user",
                        Email = "git-user@example.com"
                    },
                    Password);
                Assert.IsTrue(
                    result.Succeeded,
                    string.Join(Environment.NewLine, result.Errors.Select(static error => error.Description)));

                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public async Task<AuthenticateResult> AuthenticateAsync(string userNameOrEmail, string password)
        {
            await using var scope = ServiceProvider.CreateAsyncScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider
            };
            context.Request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userNameOrEmail}:{password}")))
                .ToString();

            var authenticationService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            return await authenticationService.AuthenticateAsync(
                context,
                GitCandyAuthenticationSchemes.GitBasic);
        }

        public async Task<AuthenticateResult> AuthenticateBearerAsync(string token)
        {
            await using var scope = ServiceProvider.CreateAsyncScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token).ToString();
            return await scope.ServiceProvider.GetRequiredService<IAuthenticationService>().AuthenticateAsync(
                context,
                GitCandyAuthenticationSchemes.PersonalAccessToken);
        }

        public async Task<CreatedPersonalAccessToken> CreateTokenAsync(params string[] scopes)
        {
            await using var scope = ServiceProvider.CreateAsyncScope();
            var token = await scope.ServiceProvider.GetRequiredService<IPersonalAccessTokenService>().CreateAsync(
                UserId,
                "test token",
                scopes,
                DateTimeOffset.UtcNow.AddHours(1));
            Assert.IsNotNull(token);
            return token;
        }

        public async Task<(int AccessFailedCount, DateTimeOffset? LockoutEnd)> GetLockoutStateAsync()
        {
            await using var scope = ServiceProvider.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
            var user = await userManager.FindByIdAsync(UserId);
            Assert.IsNotNull(user);

            return (user.AccessFailedCount, user.LockoutEnd);
        }

        public async Task<(int StatusCode, string WwwAuthenticate)> ChallengeAsync()
        {
            await using var scope = ServiceProvider.CreateAsyncScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider
            };

            var authenticationService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            await authenticationService.ChallengeAsync(
                context,
                GitCandyAuthenticationSchemes.GitBasic,
                properties: null);

            return (
                context.Response.StatusCode,
                context.Response.Headers[HeaderNames.WWWAuthenticate].ToString());
        }

        public async ValueTask DisposeAsync()
        {
            await ServiceProvider.DisposeAsync();
            TestDirectory.Delete(TempRoot);
        }
    }
}
