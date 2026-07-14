using GitCandy.Data.Configuration;
using GitCandy.Data.Identity;
using GitCandy.Remotes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class RemoteConnectionServiceTests
{
    [TestMethod]
    public async Task ConnectUserAsync_WithValidCredential_PersistsOnlyOpaqueReferenceAndDiscoversRepositories()
    {
        await using var fixture = await RemoteConnectionFixture.CreateAsync();
        var request = new RemoteUserConnectionRequest(
            RemoteProviderKind.GitHub,
            RemoteAuthenticationKind.PersonalAccessToken,
            new RemoteSecret("remote-token-must-not-be-persisted"),
            new HashSet<string>(["repo"], StringComparer.Ordinal));

        var result = await fixture.Service.ConnectUserAsync(fixture.User.Id, request);

        Assert.IsTrue(result.Diagnostic.Succeeded);
        Assert.IsNotNull(result.Connection);
        Assert.AreEqual("octocat", result.Connection.Login);
        Assert.IsFalse(typeof(RemoteConnectionSummary).GetProperties().Any(property =>
            property.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)));

        fixture.DbContext.ChangeTracker.Clear();
        var stored = await fixture.DbContext.RemoteAccountConnections.SingleAsync();
        Assert.AreEqual("vault:remote-fixture", stored.CredentialReference);
        Assert.IsFalse(JsonValues(stored).Any(value =>
            value.Contains("remote-token-must-not-be-persisted", StringComparison.Ordinal)));
        Assert.AreEqual(1, await fixture.DbContext.CredentialAuditEvents.CountAsync(item =>
            item.CredentialKind == "remote-account"
            && item.Action == "remote.connection.create"));

        var discovery = await fixture.Service.DiscoverRepositoriesAsync(
            fixture.User.Id,
            result.Connection.Id,
            null);
        Assert.IsNotNull(discovery);
        Assert.IsTrue(discovery.Diagnostic.Succeeded);
        Assert.AreEqual("octo-org/project", discovery.Page!.Repositories.Single().FullName);

        Assert.IsNull(await fixture.Service.DiscoverRepositoriesAsync(
            "different-user",
            result.Connection.Id,
            null));

        Assert.IsTrue(await fixture.Service.DisconnectUserAsync(
            fixture.User.Id,
            result.Connection.Id));
        Assert.IsTrue(fixture.Vault.Revoked);
        Assert.AreEqual(0, await fixture.DbContext.RemoteAccountConnections.CountAsync());
    }

    [TestMethod]
    public async Task ConnectUserAsync_WithMissingProviderScope_RejectsBeforeCredentialStorage()
    {
        await using var fixture = await RemoteConnectionFixture.CreateAsync();
        var request = new RemoteUserConnectionRequest(
            RemoteProviderKind.GitHub,
            RemoteAuthenticationKind.PersonalAccessToken,
            new RemoteSecret("remote-token"),
            new HashSet<string>(["public_repo"], StringComparer.Ordinal));

        var result = await fixture.Service.ConnectUserAsync(fixture.User.Id, request);

        Assert.IsFalse(result.Diagnostic.Succeeded);
        Assert.AreEqual("missing_scopes", result.Diagnostic.Code);
        Assert.IsFalse(fixture.Vault.Stored);
        Assert.AreEqual(0, await fixture.DbContext.RemoteAccountConnections.CountAsync());
    }

    private static IEnumerable<string> JsonValues(object instance) =>
        instance.GetType().GetProperties()
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.GetValue(instance) as string)
            .Where(static value => value is not null)
            .Cast<string>();

    private sealed class RemoteConnectionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;
        private readonly AsyncServiceScope _scope;

        private RemoteConnectionFixture(
            SqliteConnection connection,
            GitCandyDbContext dbContext,
            GitCandyUser user,
            FixtureRemoteCredentialVault vault,
            ServiceProvider serviceProvider,
            AsyncServiceScope scope)
        {
            _connection = connection;
            DbContext = dbContext;
            User = user;
            Vault = vault;
            _serviceProvider = serviceProvider;
            _scope = scope;
            Service = scope.ServiceProvider.GetRequiredService<IRemoteConnectionService>();
        }

        public GitCandyDbContext DbContext { get; }

        public GitCandyUser User { get; }

        public FixtureRemoteCredentialVault Vault { get; }

        public IRemoteConnectionService Service { get; }

        public static async Task<RemoteConnectionFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<GitCandyDbContext>()
                .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly("GitCandy.Data.Sqlite"))
                .Options;
            var dbContext = new GitCandyDbContext(options);
            await dbContext.Database.MigrateAsync();
            var user = new GitCandyUser
            {
                Id = Guid.NewGuid().ToString("N"),
                UserName = "remote-user",
                NormalizedUserName = "REMOTE-USER",
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();

            var vault = new FixtureRemoteCredentialVault();
            var services = new ServiceCollection();
            services.AddSingleton(dbContext);
            services.AddSingleton<IRemoteCredentialVault>(vault);
            services.AddSingleton<IRemoteRepositoryProvider, FixtureRemoteProvider>();
            services.AddSingleton(TimeProvider.System);
            services.AddGitCandyApplicationServices();
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            var scope = serviceProvider.CreateAsyncScope();
            return new RemoteConnectionFixture(
                connection,
                dbContext,
                user,
                vault,
                serviceProvider,
                scope);
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _serviceProvider.DisposeAsync();
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixtureRemoteCredentialVault : IRemoteCredentialVault
    {
        private RemoteCredential? _credential;

        public bool Stored { get; private set; }

        public bool Revoked { get; private set; }

        public Task<RemoteCredentialMetadata> StoreAsync(
            RemoteConnectionOwner owner,
            RemoteCredential credential,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stored = true;
            _credential = credential;
            return Task.FromResult(new RemoteCredentialMetadata(
                new RemoteSecretReference("vault:remote-fixture"),
                credential.AuthenticationKind,
                credential.GrantedScopes,
                DateTimeOffset.UtcNow,
                credential.ExpiresAt,
                null));
        }

        public ValueTask<RemoteCredential?> ResolveAsync(
            RemoteSecretReference reference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Revoked ? null : _credential);
        }

        public Task<RemoteCredentialMetadata?> RotateAsync(
            RemoteSecretReference reference,
            RemoteCredential replacement,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RemoteCredentialMetadata?>(null);

        public Task<bool> RevokeAsync(
            RemoteSecretReference reference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Revoked = true;
            _credential = null;
            return Task.FromResult(true);
        }
    }

    private sealed class FixtureRemoteProvider : IRemoteRepositoryProvider
    {
        public RemoteProviderKind Kind => RemoteProviderKind.GitHub;

        public Uri ServerUrl { get; } = new("https://github.com/");

        public RemoteProviderCapabilities Capabilities =>
            RemoteProviderCapabilities.AccountConnection
            | RemoteProviderCapabilities.RepositoryDiscovery;

        public IReadOnlySet<RemoteAuthenticationKind> AuthenticationKinds { get; } =
            new HashSet<RemoteAuthenticationKind>([RemoteAuthenticationKind.PersonalAccessToken]);

        public IReadOnlySet<string> GetRequiredScopes(
            RemoteAccountKind accountKind,
            RemoteRepositoryOperations operations) =>
            new HashSet<string>(["repo"], StringComparer.Ordinal);

        public Task<RemoteProviderDiagnostic> TestAsync(
            RemoteAccountConnectionContext connection,
            RemoteCredential credential,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteProviderDiagnostic(true, "ok", "Connected."));

        public Task<RemoteAccountProfile?> GetAccountAsync(
            RemoteAccountConnectionContext connection,
            RemoteCredential credential,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RemoteAccountProfile?>(new RemoteAccountProfile(
                new RemoteAccountIdentity(Kind, ServerUrl.AbsoluteUri, "account-1"),
                RemoteAccountKind.User,
                "octocat",
                "Octocat"));

        public Task<RemoteRepositoryPage> GetRepositoriesAsync(
            RemoteAccountConnectionContext connection,
            RemoteCredential credential,
            string? cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RemoteRepositoryPage(
                [new RemoteRepositoryProfile(
                    new RemoteRepositoryIdentity(Kind, ServerUrl.AbsoluteUri, "repository-1"),
                    "octo-org",
                    "project",
                    "octo-org/project",
                    new Uri("https://github.com/octo-org/project"),
                    true,
                    "main")],
                null));
    }
}
