using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class GitCandyWebServiceCollectionExtensionsTests
{
    [TestMethod]
    public async Task AddGitCandyWebShell_WithSqliteProvider_RegistersAuthSessionAndLocalizationPlaceholders()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(tempRoot, "GitCandy.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGitCandyWebShell(configuration);

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var schemeProvider = serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
            var identityScheme = await schemeProvider.GetSchemeAsync(IdentityConstants.ApplicationScheme);
            Assert.IsNotNull(identityScheme);

            var authorizationOptions = serviceProvider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
            Assert.IsNotNull(authorizationOptions.GetPolicy(GitCandyAuthorizationPolicies.Administrator));

            var sessionOptions = serviceProvider.GetRequiredService<IOptions<SessionOptions>>().Value;
            Assert.AreEqual(".GitCandy.Session", sessionOptions.Cookie.Name);

            var localizationOptions = serviceProvider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
            Assert.AreEqual("en-US", localizationOptions.DefaultRequestCulture.Culture.Name);
            Assert.IsTrue(localizationOptions.SupportedCultures?.Any(culture => culture.Name == "zh-Hans") == true);

            await using var scope = serviceProvider.CreateAsyncScope();
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<GitCandyDbContext>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<UserManager<GitCandyUser>>());
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
