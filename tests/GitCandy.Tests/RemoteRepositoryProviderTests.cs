using System.Collections.Concurrent;
using System.Net;
using GitCandy.Remotes;
using GitCandy.Web.Remotes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Tests;

[TestClass]
public sealed class RemoteRepositoryProviderTests
{
    [TestMethod]
    public async Task Providers_WithAccountAndRepositoryResponses_UseHeadersAndReturnStableProfiles()
    {
        await using var fixture = await ProviderApiFixture.CreateAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGitCandyRemoteProviders(fixture.CreateConfiguration());
        await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var providers = serviceProvider.GetServices<IRemoteRepositoryProvider>()
            .ToDictionary(provider => provider.Kind);

        Assert.AreEqual(3, providers.Count);
        foreach (var (kind, provider) in providers)
        {
            var credential = new RemoteCredential(
                RemoteAuthenticationKind.PersonalAccessToken,
                new RemoteSecret(ProviderApiFixture.Secret),
                provider.GetRequiredScopes(RemoteAccountKind.User, RemoteRepositoryOperations.Discover));
            var context = CreateContext(provider, credential);
            var diagnostic = await provider.TestAsync(context, credential);
            var account = await provider.GetAccountAsync(context, credential);
            var repositories = await provider.GetRepositoriesAsync(context, credential, null);

            Assert.IsTrue(diagnostic.Succeeded, kind.ToString());
            Assert.IsNotNull(account);
            Assert.AreEqual(kind, account.Identity.Provider);
            Assert.AreEqual("account-1", account.Identity.ExternalId);
            Assert.AreEqual(kind, repositories.Repositories.Single().Identity.Provider);
            Assert.AreEqual("repository-1", repositories.Repositories.Single().Identity.ExternalId);
            Assert.AreEqual("2", repositories.NextCursor);
        }

        Assert.IsTrue(fixture.ObservedRequests.TryGetValue(RemoteProviderKind.GitHub, out var gitHub));
        Assert.AreEqual($"Bearer {ProviderApiFixture.Secret}", gitHub.Authorization);
        Assert.IsTrue(fixture.ObservedRequests.TryGetValue(RemoteProviderKind.GitLab, out var gitLab));
        Assert.AreEqual(ProviderApiFixture.Secret, gitLab.PrivateToken);
        Assert.IsTrue(fixture.ObservedRequests.TryGetValue(RemoteProviderKind.Gitee, out var gitee));
        Assert.AreEqual($"token {ProviderApiFixture.Secret}", gitee.Authorization);
        Assert.IsFalse(fixture.ObservedRequests.Values.Any(request =>
            request.Uri.Contains(ProviderApiFixture.Secret, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Provider_WithAuthenticatedRedirect_RejectsRedirectWithoutFollowingIt()
    {
        await using var fixture = await ProviderApiFixture.CreateAsync();
        var configuration = fixture.CreateConfiguration();
        configuration["GitCandy:Remotes:GitHub:ApiBaseUrl"] = $"{fixture.BaseAddress}redirect-api";
        configuration["GitCandy:Remotes:GitLab:Enabled"] = "false";
        configuration["GitCandy:Remotes:Gitee:Enabled"] = "false";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGitCandyRemoteProviders(configuration);
        await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var provider = serviceProvider.GetRequiredService<IRemoteRepositoryProvider>();
        var credential = new RemoteCredential(
            RemoteAuthenticationKind.PersonalAccessToken,
            new RemoteSecret(ProviderApiFixture.Secret),
            ["repo"]);

        var diagnostic = await provider.TestAsync(CreateContext(provider, credential), credential);

        Assert.IsFalse(diagnostic.Succeeded);
        Assert.AreEqual("redirect_rejected", diagnostic.Code);
        Assert.AreEqual(0, fixture.RedirectTargetRequests);
    }

    private static RemoteAccountConnectionContext CreateContext(
        IRemoteRepositoryProvider provider,
        RemoteCredential credential) => new(
            1,
            new RemoteConnectionOwner(RemoteConnectionOwnerKind.User, "user-1"),
            new RemoteAccountProfile(
                new RemoteAccountIdentity(provider.Kind, provider.ServerUrl.AbsoluteUri, "account-1"),
                RemoteAccountKind.User,
                "fixture",
                null),
            credential.AuthenticationKind,
            new RemoteSecretReference("vault:fixture"),
            credential.GrantedScopes,
            true);

    private sealed class ProviderApiFixture : IAsyncDisposable
    {
        public const string Secret = "provider-test-secret";
        private readonly WebApplication _app;

        private ProviderApiFixture(WebApplication app, Uri baseAddress)
        {
            _app = app;
            BaseAddress = baseAddress;
        }

        public Uri BaseAddress { get; private set; }

        public ConcurrentDictionary<RemoteProviderKind, ObservedRequest> ObservedRequests { get; } = new();

        public int RedirectTargetRequests { get; private set; }

        public static async Task<ProviderApiFixture> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            var fixture = new ProviderApiFixture(app, new Uri("http://127.0.0.1"));
            fixture.MapEndpoints();
            await app.StartAsync();
            fixture.BaseAddress = new Uri(GetServerAddress(app));
            return fixture;
        }

        public IConfigurationRoot CreateConfiguration()
        {
            var root = BaseAddress.AbsoluteUri.TrimEnd('/');
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Remotes:GitHub:ServerUrl"] = $"{root}/github",
                    ["GitCandy:Remotes:GitHub:ApiBaseUrl"] = $"{root}/github-api",
                    ["GitCandy:Remotes:GitLab:ServerUrl"] = $"{root}/gitlab",
                    ["GitCandy:Remotes:GitLab:ApiBaseUrl"] = $"{root}/gitlab-api",
                    ["GitCandy:Remotes:Gitee:ServerUrl"] = $"{root}/gitee",
                    ["GitCandy:Remotes:Gitee:ApiBaseUrl"] = $"{root}/gitee-api"
                })
                .Build();
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private void MapEndpoints()
        {
            MapProvider(
                "/github-api",
                RemoteProviderKind.GitHub,
                """{"id":"account-1","login":"octocat","name":"Octocat","type":"User"}""",
                """[{"id":"repository-1","name":"project","full_name":"octo/project","html_url":"https://github.com/octo/project","private":true,"default_branch":"main","owner":{"login":"octo"}}]""");
            MapProvider(
                "/gitlab-api",
                RemoteProviderKind.GitLab,
                """{"id":"account-1","username":"octocat","name":"Octocat"}""",
                """[{"id":"repository-1","path":"project","path_with_namespace":"octo/project","web_url":"https://gitlab.com/octo/project","visibility":"private","default_branch":"main"}]""");
            MapProvider(
                "/gitee-api",
                RemoteProviderKind.Gitee,
                """{"id":"account-1","login":"octocat","name":"Octocat"}""",
                """[{"id":"repository-1","path":"project","full_name":"octo/project","html_url":"https://gitee.com/octo/project","private":true,"default_branch":"main"}]""");
            _app.MapGet("/redirect-api/user", (HttpContext context) =>
            {
                context.Response.StatusCode = StatusCodes.Status302Found;
                context.Response.Headers.Location = "/redirect-target";
                return Task.CompletedTask;
            });
            _app.MapGet("/redirect-target", () =>
            {
                RedirectTargetRequests++;
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            });
        }

        private void MapProvider(
            string prefix,
            RemoteProviderKind kind,
            string accountJson,
            string repositoriesJson)
        {
            _app.MapGet($"{prefix}/user", async context =>
            {
                Observe(kind, context.Request);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(accountJson);
            });
            _app.MapGet($"{prefix}/user/repos", WriteRepositoriesAsync);
            _app.MapGet($"{prefix}/projects", WriteRepositoriesAsync);

            async Task WriteRepositoriesAsync(HttpContext context)
            {
                Observe(kind, context.Request);
                context.Response.ContentType = "application/json";
                context.Response.Headers["X-Next-Page"] = "2";
                await context.Response.WriteAsync(repositoriesJson);
            }
        }

        private void Observe(RemoteProviderKind kind, HttpRequest request)
        {
            ObservedRequests[kind] = new ObservedRequest(
                request.GetDisplayUrl(),
                request.Headers.Authorization.ToString(),
                request.Headers["PRIVATE-TOKEN"].ToString());
        }

        private static string GetServerAddress(WebApplication app)
        {
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?
                .Addresses;
            Assert.IsNotNull(addresses);
            return addresses.Single();
        }
    }

    private sealed record ObservedRequest(string Uri, string Authorization, string PrivateToken);
}
