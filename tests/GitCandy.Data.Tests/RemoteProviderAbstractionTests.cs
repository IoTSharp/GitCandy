using System.Text.Json;
using GitCandy.Data.Configuration;
using GitCandy.Remotes;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class RemoteProviderAbstractionTests
{
    [TestMethod]
    public void RemoteRepositoryIdentity_WithRemoteRename_PreservesStableIdentity()
    {
        var originalIdentity = new RemoteRepositoryIdentity(
            RemoteProviderKind.GitHub,
            "https://GITHUB.com/",
            "R_kgDOExample");
        var renamedIdentity = new RemoteRepositoryIdentity(
            RemoteProviderKind.GitHub,
            "https://github.com",
            "R_kgDOExample");
        var original = new RemoteRepositoryProfile(
            originalIdentity,
            "old-owner",
            "old-name",
            "old-owner/old-name",
            new Uri("https://github.com/old-owner/old-name"),
            true,
            "main");
        var renamed = original with
        {
            Identity = renamedIdentity,
            OwnerLogin = "new-owner",
            Name = "new-name",
            FullName = "new-owner/new-name",
            WebUrl = new Uri("https://github.com/new-owner/new-name")
        };

        Assert.AreEqual(original.Identity, renamed.Identity);
        Assert.AreNotEqual(original.FullName, renamed.FullName);
        Assert.AreEqual("https://github.com/", original.Identity.ServerUrl.AbsoluteUri);
    }

    [TestMethod]
    public void RemoteScopePolicy_WithMissingProviderScope_ReturnsOnlyMissingScopes()
    {
        var validation = RemoteScopePolicy.Validate(
            ["read_repository", "read_user"],
            ["read_user", "write_repository", "read_repository"]);

        Assert.IsFalse(validation.Satisfied);
        CollectionAssert.AreEqual(new[] { "write_repository" }, validation.MissingScopes.ToArray());
    }

    [TestMethod]
    public void RemoteCredential_WithSecret_ToStringAlwaysRedactsSecret()
    {
        const string token = "provider-token-that-must-not-leak";
        var credential = new RemoteCredential(
            RemoteAuthenticationKind.OAuth,
            new RemoteSecret(token),
            ["repo:read"]);

        Assert.AreEqual(token, credential.Secret.Value);
        Assert.AreEqual("[REDACTED]", credential.Secret.ToString());
        Assert.IsFalse(credential.ToString().Contains(token, StringComparison.Ordinal));
        StringAssert.Contains(credential.ToString(), "[REDACTED]");
        Assert.IsFalse(JsonSerializer.Serialize(credential.Secret).Contains(token, StringComparison.Ordinal));
        Assert.IsFalse(JsonSerializer.Serialize(credential).Contains(token, StringComparison.Ordinal));
    }

    [TestMethod]
    public void RemoteConnectionContext_WithUserAndServiceAccount_DistinguishesBothDimensions()
    {
        var userConnection = CreateConnection(
            RemoteConnectionOwnerKind.User,
            "user-1",
            RemoteAccountKind.User);
        var serviceConnection = CreateConnection(
            RemoteConnectionOwnerKind.Team,
            "team-1",
            RemoteAccountKind.ServiceAccount);

        Assert.AreEqual(RemoteConnectionOwnerKind.User, userConnection.Owner.Kind);
        Assert.AreEqual(RemoteAccountKind.User, userConnection.Account.Kind);
        Assert.AreEqual(RemoteConnectionOwnerKind.Team, serviceConnection.Owner.Kind);
        Assert.AreEqual(RemoteAccountKind.ServiceAccount, serviceConnection.Account.Kind);
    }

    [TestMethod]
    public async Task AddGitCandyApplicationServices_WithRemoteProvider_ResolvesCatalogAndSafeVaultDefault()
    {
        var services = new ServiceCollection();
        services.AddGitCandyApplicationServices();
        services.AddSingleton<IRemoteRepositoryProvider, FixtureRemoteProvider>();

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IRemoteProviderCatalog>();
        var vault = scope.ServiceProvider.GetRequiredService<IRemoteCredentialVault>();

        Assert.AreEqual(RemoteProviderKind.GitHub, catalog.AvailableProviders.Single());
        Assert.IsInstanceOfType<FixtureRemoteProvider>(catalog.Get(RemoteProviderKind.GitHub));
        Assert.IsNull(catalog.Get(RemoteProviderKind.GitLab));
        Assert.IsNull(await vault.ResolveAsync(new RemoteSecretReference("vault:remote/fixture")));
    }

    [TestMethod]
    public async Task RemoteProviderCatalog_WithDuplicateProviderKinds_RejectsAmbiguousRegistration()
    {
        var services = new ServiceCollection();
        services.AddGitCandyApplicationServices();
        services.AddSingleton<IRemoteRepositoryProvider, FixtureRemoteProvider>();
        services.AddSingleton<IRemoteRepositoryProvider, DuplicateGitHubProvider>();

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<IRemoteProviderCatalog>());
    }

    private static RemoteAccountConnectionContext CreateConnection(
        RemoteConnectionOwnerKind ownerKind,
        string ownerId,
        RemoteAccountKind accountKind) => new(
            1,
            new RemoteConnectionOwner(ownerKind, ownerId),
            new RemoteAccountProfile(
                new RemoteAccountIdentity(RemoteProviderKind.GitHub, "https://github.com", "account-1"),
                accountKind,
                "fixture",
                "Fixture"),
            RemoteAuthenticationKind.OAuth,
            new RemoteSecretReference("vault:remote/fixture"),
            new HashSet<string>(["repo:read"], StringComparer.Ordinal),
            true);

    private class FixtureRemoteProvider : IRemoteRepositoryProvider
    {
        public RemoteProviderKind Kind => RemoteProviderKind.GitHub;

        public Uri ServerUrl { get; } = new("https://github.com/");

        public RemoteProviderCapabilities Capabilities =>
            RemoteProviderCapabilities.AccountConnection
            | RemoteProviderCapabilities.RepositoryDiscovery;

        public IReadOnlySet<RemoteAuthenticationKind> AuthenticationKinds { get; } =
            new HashSet<RemoteAuthenticationKind>
            {
                RemoteAuthenticationKind.App,
                RemoteAuthenticationKind.OAuth
            };

        public IReadOnlySet<string> GetRequiredScopes(
            RemoteAccountKind accountKind,
            RemoteRepositoryOperations operations) =>
            new HashSet<string>(["repo:read"], StringComparer.Ordinal);

        public Task<RemoteProviderDiagnostic> TestAsync(
            RemoteAccountConnectionContext connection,
            RemoteCredential credential,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteProviderDiagnostic(true, "ok", "Connected."));

        public Task<RemoteAccountProfile?> GetAccountAsync(
            RemoteAccountConnectionContext connection,
            RemoteCredential credential,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RemoteAccountProfile?>(connection.Account);

        public Task<RemoteRepositoryPage> GetRepositoriesAsync(
            RemoteAccountConnectionContext connection,
            RemoteCredential credential,
            string? cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteRepositoryPage([], null));
    }

    private sealed class DuplicateGitHubProvider : FixtureRemoteProvider;
}
