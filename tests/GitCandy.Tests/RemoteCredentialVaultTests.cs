using GitCandy.Configuration;
using GitCandy.Remotes;
using GitCandy.Web.Remotes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Tests;

[TestClass]
public sealed class RemoteCredentialVaultTests
{
    [TestMethod]
    public async Task CredentialVault_AcrossHostRestart_EncryptsResolvesAndRevokesSecret()
    {
        var root = TestDirectory.Create();
        var keyPath = Path.Combine(root, "keys");
        const string secretValue = "remote-provider-secret-value";
        try
        {
            RemoteSecretReference reference;
            await using (var firstProvider = CreateServiceProvider(root, keyPath))
            {
                var vault = firstProvider.GetRequiredService<IRemoteCredentialVault>();
                var metadata = await vault.StoreAsync(
                    new RemoteConnectionOwner(RemoteConnectionOwnerKind.User, "user-1"),
                    new RemoteCredential(
                        RemoteAuthenticationKind.PersonalAccessToken,
                        new RemoteSecret(secretValue),
                        ["repo"]));
                reference = metadata.Reference;
                var resolved = await vault.ResolveAsync(reference);
                Assert.IsNotNull(resolved);
                Assert.AreEqual(secretValue, resolved.Secret.Value);
            }

            var credentialFiles = Directory.GetFiles(
                Path.Combine(keyPath, "remote-credentials"),
                "*.credential",
                SearchOption.TopDirectoryOnly);
            Assert.AreEqual(1, credentialFiles.Length);
            Assert.IsFalse((await File.ReadAllTextAsync(credentialFiles[0]))
                .Contains(secretValue, StringComparison.Ordinal));

            await using (var restartedProvider = CreateServiceProvider(root, keyPath))
            {
                var vault = restartedProvider.GetRequiredService<IRemoteCredentialVault>();
                var resolved = await vault.ResolveAsync(reference);
                Assert.IsNotNull(resolved);
                Assert.AreEqual(secretValue, resolved.Secret.Value);
                Assert.IsTrue(await vault.RevokeAsync(reference));
                Assert.IsNull(await vault.ResolveAsync(reference));
            }
        }
        finally
        {
            TestDirectory.Delete(root);
        }
    }

    private static ServiceProvider CreateServiceProvider(string root, string keyPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Remotes:GitHub:Enabled"] = "false",
                ["GitCandy:Remotes:GitLab:Enabled"] = "false",
                ["GitCandy:Remotes:Gitee:Enabled"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGitCandyApplicationPaths>(new FixtureApplicationPaths(root, keyPath));
        services.AddSingleton(TimeProvider.System);
        services.AddDataProtection()
            .SetApplicationName("GitCandy.RemoteCredentialVault.Tests")
            .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        services.AddGitCandyRemoteProviders(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class FixtureApplicationPaths(string root, string keyPath) : IGitCandyApplicationPaths
    {
        public string ContentRootPath { get; } = root;
        public string? WebRootPath => null;
        public string UserConfigurationPath { get; } = Path.Combine(root, "config.xml");
        public string RepositoryPath { get; } = Path.Combine(root, "repositories");
        public string CachePath { get; } = Path.Combine(root, "cache");
        public string GitCorePath => string.Empty;
        public string SshHostKeyPath { get; } = Path.Combine(root, "ssh-host-key.xml");
        public string DataProtectionKeysPath { get; } = keyPath;
        public string ResolveContentPath(string configuredPath) => Path.GetFullPath(configuredPath, ContentRootPath);
        public string ResolveWebRootPath(string configuredPath) => throw new InvalidOperationException();
        public string ResolvePathWithinRepositoryRoot(string path) => Path.GetFullPath(path, RepositoryPath);
        public string ResolvePathWithinCacheRoot(string path) => Path.GetFullPath(path, CachePath);
    }
}
