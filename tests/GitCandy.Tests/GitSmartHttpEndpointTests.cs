using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GitCandy.Authentication;
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
using Microsoft.Net.Http.Headers;

namespace GitCandy.Tests;

[TestClass]
public sealed class GitSmartHttpEndpointTests
{
    private const string Password = "M6-endpoint-Password-1!";

    [TestMethod]
    public async Task Smart_WithCompatibleRoutesAndProtocolVersions_ReturnsExactAdvertisementHeaders()
    {
        await using var fixture = await EndpointFixture.CreateAsync();
        await fixture.SeedRepositoryAsync("public-demo", allowAnonymousRead: true);

        using var dotGitResponse = await fixture.Client.GetAsync(
            "/git/public-demo.git/info/refs?service=git-upload-pack");
        Assert.AreEqual(HttpStatusCode.OK, dotGitResponse.StatusCode);
        Assert.AreEqual(
            "application/x-git-upload-pack-advertisement",
            dotGitResponse.Content.Headers.ContentType?.MediaType);
        AssertNoCacheHeaders(dotGitResponse);
        var dotGitBody = await dotGitResponse.Content.ReadAsStringAsync();
        StringAssert.StartsWith(dotGitBody, "001E# service=git-upload-pack\n0000");

        using var noSuffixRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/git/public-demo/info/refs?service=git-upload-pack");
        noSuffixRequest.Headers.TryAddWithoutValidation("Git-Protocol", "version=2");
        using var noSuffixResponse = await fixture.Client.SendAsync(noSuffixRequest);
        Assert.AreEqual(HttpStatusCode.OK, noSuffixResponse.StatusCode);
        var noSuffixBody = await noSuffixResponse.Content.ReadAsStringAsync();
        StringAssert.StartsWith(noSuffixBody, "000eversion 2\n");
        Assert.IsFalse(noSuffixBody.Contains("# service=", StringComparison.Ordinal));
        Assert.AreEqual("version=2", fixture.Backend.LastRequest?.ProtocolVersion);
    }

    [TestMethod]
    public async Task Smart_WithGzipBodyAndMismatchedQuery_StreamsDecompressedBodyToVerbBackend()
    {
        await using var fixture = await EndpointFixture.CreateAsync();
        await fixture.SeedRepositoryAsync("public-demo", allowAnonymousRead: true);
        var body = Encoding.UTF8.GetBytes(new string('x', 256 * 1024));

        await using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            await gzip.WriteAsync(body);
        }

        using var content = new ByteArrayContent(compressed.ToArray());
        content.Headers.ContentEncoding.Add("gzip");
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/x-git-upload-pack-request");
        using var response = await fixture.Client.PostAsync(
            "/git/public-demo.git/git-upload-pack?service=git-receive-pack",
            content);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        CollectionAssert.AreEqual(body, await response.Content.ReadAsByteArrayAsync());
        Assert.AreEqual(GitTransportService.UploadPack, fixture.Backend.LastRequest?.Service);
        Assert.AreEqual(
            "application/x-git-upload-pack-result",
            response.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task Smart_WithAuthenticationAndPermissionFailures_ReturnsGitClientStatusCodes()
    {
        await using var fixture = await EndpointFixture.CreateAsync();
        await fixture.SeedRepositoryAsync("private-demo", isPrivate: true);
        await fixture.CreateUserAsync("denied-user", Password);

        using var anonymousResponse = await fixture.Client.GetAsync(
            "/git/private-demo.git/info/refs?service=git-upload-pack");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.AreEqual(
            "Basic realm=\"GitCandy\", charset=\"UTF-8\"",
            anonymousResponse.Headers.WwwAuthenticate.Single().ToString());

        using var deniedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/git/private-demo.git/info/refs?service=git-upload-pack");
        deniedRequest.Headers.Authorization = CreateBasicHeader("denied-user", Password);
        using var deniedResponse = await fixture.Client.SendAsync(deniedRequest);
        Assert.AreEqual(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var missingResponse = await fixture.Client.GetAsync(
            "/git/missing-demo.git/info/refs?service=git-upload-pack");
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);

        using var unsupportedResponse = await fixture.Client.GetAsync(
            "/git/private-demo.git/info/refs?service=git-upload-archive");
        Assert.AreEqual(HttpStatusCode.Forbidden, unsupportedResponse.StatusCode);

        using var traversalResponse = await fixture.Client.GetAsync(
            "/git/%2e%2e.git/info/refs?service=git-upload-pack");
        Assert.AreEqual(HttpStatusCode.NotFound, traversalResponse.StatusCode);
    }

    [TestMethod]
    public async Task Smart_WithConfiguredRequestLimit_ReturnsPayloadTooLargeBeforeBackend()
    {
        await using var fixture = await EndpointFixture.CreateAsync(maxRequestBodySize: 32);
        await fixture.SeedRepositoryAsync("public-demo", allowAnonymousRead: true);

        using var response = await fixture.Client.PostAsync(
            "/git/public-demo.git/git-upload-pack",
            new ByteArrayContent(new byte[64]));

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.IsNull(fixture.Backend.LastRequest);
    }

    [TestMethod]
    public async Task Smart_WithConfiguredTimeout_CancelsBackendAndReturnsGatewayTimeout()
    {
        await using var fixture = await EndpointFixture.CreateAsync(
            requestTimeout: TimeSpan.FromMilliseconds(100),
            delayBackend: true);
        await fixture.SeedRepositoryAsync("public-demo", allowAnonymousRead: true);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/git/public-demo.git/info/refs?service=git-upload-pack");
        request.Headers.TryAddWithoutValidation("Git-Protocol", "version=2");
        using var response = await fixture.Client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.IsTrue(fixture.Backend.CancellationObserved);
    }

    private static AuthenticationHeaderValue CreateBasicHeader(string userName, string password)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{userName}:{password}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private static void AssertNoCacheHeaders(HttpResponseMessage response)
    {
        Assert.IsTrue(response.Headers.CacheControl?.NoCache);
        Assert.IsTrue(response.Headers.CacheControl?.MustRevalidate);
        Assert.AreEqual(TimeSpan.Zero, response.Headers.CacheControl?.MaxAge);
        Assert.AreEqual("no-cache", response.Headers.GetValues(HeaderNames.Pragma).Single());
        Assert.AreEqual(
            "Fri, 01 Jan 1980 00:00:00 GMT",
            response.Content.Headers.GetValues(HeaderNames.Expires).Single());
    }

    private sealed class EndpointFixture : IAsyncDisposable
    {
        private EndpointFixture(
            WebApplication app,
            HttpClient client,
            EchoGitTransportBackend backend,
            string repositoryRoot,
            string tempRoot)
        {
            App = app;
            Client = client;
            Backend = backend;
            RepositoryRoot = repositoryRoot;
            TempRoot = tempRoot;
        }

        private WebApplication App { get; }

        public HttpClient Client { get; }

        public EchoGitTransportBackend Backend { get; }

        private string RepositoryRoot { get; }

        private string TempRoot { get; }

        public static async Task<EndpointFixture> CreateAsync(
            long maxRequestBodySize = 4L * 1024 * 1024 * 1024,
            TimeSpan? requestTimeout = null,
            bool delayBackend = false)
        {
            var tempRoot = TestDirectory.Create();
            var repositoryRoot = Path.Combine(tempRoot, "Repositories");
            Directory.CreateDirectory(repositoryRoot);
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
                ["GitCandy:Application:LogPathFormat"] = Path.Combine(tempRoot, "Logs", "{0}.log"),
                ["GitCandy:Application:EnableSsh"] = "false",
                ["GitCandy:GitHttp:MaxRequestBodySize"] = maxRequestBodySize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["GitCandy:GitHttp:RequestTimeout"] = (requestTimeout ?? TimeSpan.FromMinutes(1)).ToString("c")
            });
            builder.Services.AddGitCandyWebShell(builder.Configuration);
            builder.Services.AddControllersWithViews()
                .AddApplicationPart(typeof(GitController).Assembly);
            builder.Services.RemoveAll<IHostedService>();
            builder.Services.RemoveAll<IGitTransportBackend>();
            var backend = new EchoGitTransportBackend(delayBackend);
            builder.Services.AddSingleton<IGitTransportBackend>(backend);

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
                }

                await app.StartAsync();
                var client = new HttpClient(
                    new HttpClientHandler { AllowAutoRedirect = false })
                {
                    BaseAddress = new Uri(GetServerAddress(app))
                };
                return new EndpointFixture(app, client, backend, repositoryRoot, tempRoot);
            }
            catch
            {
                await app.DisposeAsync();
                TestDirectory.Delete(tempRoot);
                throw;
            }
        }

        public async Task SeedRepositoryAsync(
            string name,
            bool isPrivate = false,
            bool allowAnonymousRead = false)
        {
            Directory.CreateDirectory(Path.Combine(RepositoryRoot, name));
            await using var scope = App.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            dbContext.Repositories.Add(new GitCandyRepository
            {
                Name = name,
                Description = $"{name} endpoint fixture",
                CreatedAtUtc = DateTime.UtcNow,
                IsPrivate = isPrivate,
                AllowAnonymousRead = allowAnonymousRead
            });
            await dbContext.SaveChangesAsync();
        }

        public async Task CreateUserAsync(string userName, string password)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>();
            var result = await userManager.CreateAsync(
                new GitCandyUser
                {
                    UserName = userName,
                    Email = $"{userName}@example.com",
                    DisplayName = userName
                },
                password);
            Assert.IsTrue(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Description)));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            TestDirectory.Delete(TempRoot);
        }
    }

    private sealed class EchoGitTransportBackend(bool delay) : IGitTransportBackend
    {
        private readonly bool _delay = delay;

        public GitTransportRequest? LastRequest { get; private set; }

        public bool CancellationObserved { get; private set; }

        public void EnsureRepositoryExists(GitRepositoryContext repository)
        {
            if (!Directory.Exists(repository.RepositoryPath))
            {
                throw new GitRepositoryNotFoundException(repository.RepositoryName);
            }
        }

        public async Task ExecuteAsync(
            GitTransportRequest request,
            Stream input,
            Stream output,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (_delay)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    throw;
                }
            }

            if (request.AdvertiseRefs)
            {
                var advertisement = request.ProtocolVersion == "version=2"
                    ? "000eversion 2\n0000"
                    : "0000";
                await output.WriteAsync(
                    Encoding.ASCII.GetBytes(advertisement),
                    cancellationToken);
                return;
            }

            await input.CopyToAsync(output, cancellationToken);
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
