using System.Net;
using GitCandy.Configuration;
using GitCandy.Help;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Tests;

[TestClass]
public sealed class HelpEndpointTests
{
    [TestMethod]
    public async Task HelpEndpoint_WithAnonymousGetAndHead_ServesGeneratedContentWithSecurityHeaders()
    {
        await using var fixture = await HelpFixture.CreateAsync();

        using var response = await fixture.Client.GetAsync("/help/");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.AreEqual("no-cache", response.Headers.CacheControl?.ToString());
        StringAssert.Contains(response.Headers.GetValues("Content-Security-Policy").Single(), "default-src 'none'");
        Assert.AreEqual("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Generated help home");

        using var headRequest = new HttpRequestMessage(HttpMethod.Head, "/help/current/");
        using var headResponse = await fixture.Client.SendAsync(headRequest);
        Assert.AreEqual(HttpStatusCode.OK, headResponse.StatusCode);
        Assert.AreEqual(fixture.CurrentPageLength, headResponse.Content.Headers.ContentLength);
        Assert.AreEqual(string.Empty, await headResponse.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task HelpEndpoint_WithPathBaseAndExtensionlessPermalink_ServesTheExpectedPage()
    {
        await using var fixture = await HelpFixture.CreateAsync("/gitcandy");

        using var response = await fixture.Client.GetAsync("/gitcandy/help/current");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("public, max-age=300", response.Headers.CacheControl?.ToString());
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Current help page");

        using var redirectClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = fixture.Client.BaseAddress
        };
        using var redirectResponse = await redirectClient.GetAsync("/gitcandy/help");
        Assert.AreEqual(HttpStatusCode.PermanentRedirect, redirectResponse.StatusCode);
        Assert.AreEqual("/gitcandy/help/", redirectResponse.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task HelpEndpoint_WithMissingTraversalOrUnsupportedFile_ReturnsSafeNotFound()
    {
        await using var fixture = await HelpFixture.CreateAsync();

        foreach (var path in new[] { "/help/missing", "/help/assets/blocked.exe" })
        {
            using var response = await fixture.Client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, path);
            Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString(), path);
            Assert.AreEqual("Help page not found.", await response.Content.ReadAsStringAsync(), path);
        }

        using var traversalResponse = await fixture.Client.GetAsync("/help/%2e%2e/secret.txt");
        Assert.AreEqual(HttpStatusCode.NotFound, traversalResponse.StatusCode);
        Assert.AreNotEqual("secret", await traversalResponse.Content.ReadAsStringAsync());

        using var postResponse = await fixture.Client.PostAsync("/help", content: null);
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, postResponse.StatusCode);
    }

    private sealed class HelpFixture : IAsyncDisposable
    {
        private HelpFixture(WebApplication app, HttpClient client, string tempRoot, long currentPageLength)
        {
            App = app;
            Client = client;
            TempRoot = tempRoot;
            CurrentPageLength = currentPageLength;
        }

        private WebApplication App { get; }
        private string TempRoot { get; }
        public HttpClient Client { get; }
        public long CurrentPageLength { get; }

        public static async Task<HelpFixture> CreateAsync(string? pathBase = null)
        {
            var tempRoot = TestDirectory.Create();
            var helpRoot = Path.Combine(tempRoot, "help");
            Directory.CreateDirectory(Path.Combine(helpRoot, "current"));
            Directory.CreateDirectory(Path.Combine(helpRoot, "assets"));
            await File.WriteAllTextAsync(Path.Combine(helpRoot, "index.html"), "<h1>Generated help home</h1>");
            var currentPage = "<h1>Current help page</h1>";
            await File.WriteAllTextAsync(Path.Combine(helpRoot, "current", "index.html"), currentPage);
            await File.WriteAllTextAsync(Path.Combine(helpRoot, "assets", "blocked.exe"), "not public");
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "secret.txt"), "secret");

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.Configure<HelpContentOptions>(options => options.ContentPath = helpRoot);
            var app = builder.Build();
            if (!string.IsNullOrEmpty(pathBase))
            {
                app.UsePathBase(pathBase);
            }
            app.MapGitCandyHelp();

            try
            {
                await app.StartAsync();
                var client = new HttpClient { BaseAddress = new Uri(GetServerAddress(app)) };
                return new HelpFixture(app, client, tempRoot, System.Text.Encoding.UTF8.GetByteCount(currentPage));
            }
            catch
            {
                await app.DisposeAsync();
                TestDirectory.Delete(tempRoot);
                throw;
            }
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
    }
}
