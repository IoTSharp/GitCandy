using System.Net;
using GitCandy.Configuration;
using GitCandy.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GitCandy.Tests;

[TestClass]
public sealed class CompatibilityRouteTests
{
    [TestMethod]
    public async Task MapGitCandyCompatibilityRoutes_WithPlaceholderPaths_ReturnsExpectedResponses()
    {
        await using var app = await StartRouteTestAppAsync();
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(GetServerAddress(app)),
        };

        using var homeResponse = await httpClient.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.OK, homeResponse.StatusCode);

        using var repositoryResponse = await httpClient.GetAsync("/Repository");
        Assert.AreEqual(HttpStatusCode.OK, repositoryResponse.StatusCode);
        var repositoryContent = await repositoryResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(repositoryContent, "Repository/Index");

    }

    [TestMethod]
    public async Task MapGitCandyCompatibilityRoutes_WithGitSmartHttpPaths_ReturnsNotImplementedPlaceholder()
    {
        await using var app = await StartRouteTestAppAsync();
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(GetServerAddress(app)),
        };

        using var dotGitResponse = await httpClient.GetAsync("/git/sample.git/info/refs");
        Assert.AreEqual(HttpStatusCode.NotImplemented, dotGitResponse.StatusCode);
        var dotGitContent = await dotGitResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(dotGitContent, "sample/info/refs");

        using var legacyGitResponse = await httpClient.GetAsync("/git/sample/info/refs");
        Assert.AreEqual(HttpStatusCode.NotImplemented, legacyGitResponse.StatusCode);
        var legacyGitContent = await legacyGitResponse.Content.ReadAsStringAsync();
        StringAssert.Contains(legacyGitContent, "sample/info/refs");
    }

    private static async Task<WebApplication> StartRouteTestAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = GetWebProjectRoot(),
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseKestrel();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(CompatibilityController).Assembly);

        var app = builder.Build();
        app.MapGitCandyCompatibilityRoutes();

        await app.StartAsync();
        return app;
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
