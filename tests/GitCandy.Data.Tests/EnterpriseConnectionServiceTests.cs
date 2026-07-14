using GitCandy.Application;
using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Enterprise;
using GitCandy.Teams;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class EnterpriseConnectionServiceTests
{
    [TestMethod]
    public async Task SaveAsync_WithOwnerAndSecretReference_PersistsOnlyReferenceAndAllowsLeaderRead()
    {
        await using var fixture = await EnterpriseFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IEnterpriseConnectionService>();

        var saved = await service.SaveAsync(
            EnterpriseFixture.TeamName,
            NewEdit(),
            EnterpriseFixture.OwnerId,
            actorIsSystemAdministrator: false);

        Assert.IsNotNull(saved);
        Assert.AreEqual("env:ENTRA_SECRET", saved.SecretReference);
        Assert.IsFalse(saved.SecretReference.Contains("resolved-value", StringComparison.Ordinal));
        var leaderView = await service.GetForTeamAsync(
            EnterpriseFixture.TeamName,
            EnterpriseFixture.LeaderId,
            actorIsSystemAdministrator: false);
        Assert.IsNotNull(leaderView);
        Assert.HasCount(1, leaderView);

        var denied = await service.SaveAsync(
            EnterpriseFixture.TeamName,
            NewEdit(name: "leader-edit"),
            EnterpriseFixture.LeaderId,
            actorIsSystemAdministrator: false);
        Assert.IsNull(denied);
    }

    [TestMethod]
    public async Task SaveAsync_WithSecretInConfigurationJson_RejectsConnection()
    {
        await using var fixture = await EnterpriseFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IEnterpriseConnectionService>();

        var saved = await service.SaveAsync(
            EnterpriseFixture.TeamName,
            NewEdit(configurationJson: "{\"clientSecret\":\"must-not-persist\"}"),
            EnterpriseFixture.OwnerId,
            actorIsSystemAdministrator: false);

        Assert.IsNull(saved);
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        Assert.AreEqual(0, await dbContext.EnterpriseConnections.CountAsync());
    }

    [TestMethod]
    public async Task TestAsync_WithResolvedSecret_ReturnsSanitizedDiagnosticAndAuditsResult()
    {
        await using var fixture = await EnterpriseFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IEnterpriseConnectionService>();
        var saved = await service.SaveAsync(
            EnterpriseFixture.TeamName,
            NewEdit(),
            EnterpriseFixture.OwnerId,
            actorIsSystemAdministrator: false);
        Assert.IsNotNull(saved);

        var diagnostic = await service.TestAsync(
            EnterpriseFixture.TeamName,
            saved.Id,
            EnterpriseFixture.OwnerId,
            actorIsSystemAdministrator: false);

        Assert.IsNotNull(diagnostic);
        Assert.IsTrue(diagnostic.Succeeded);
        Assert.AreEqual("connected", diagnostic.Code);
        Assert.IsFalse(diagnostic.Message.Contains("resolved-value", StringComparison.Ordinal));
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var connection = await dbContext.EnterpriseConnections.SingleAsync();
        Assert.AreEqual(EnterpriseConnectionStatus.Healthy, connection.Status);
        Assert.IsNull(connection.LastErrorCode);
        Assert.AreEqual(
            1,
            await dbContext.TeamAuditEvents.CountAsync(item => item.Action == "enterprise.connection.test"));
    }

    [TestMethod]
    public async Task SaveChangesAsync_WithDuplicateExternalId_RejectsDatabaseWrite()
    {
        await using var fixture = await EnterpriseFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IEnterpriseConnectionService>();
        var saved = await service.SaveAsync(
            EnterpriseFixture.TeamName,
            NewEdit(),
            EnterpriseFixture.OwnerId,
            actorIsSystemAdministrator: false);
        Assert.IsNotNull(saved);

        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        dbContext.EnterpriseExternalIdentities.AddRange(
            NewIdentity(saved.Id, "stable-subject"),
            NewIdentity(saved.Id, "stable-subject"));

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    private static EnterpriseConnectionEdit NewEdit(
        string name = "entra",
        string? configurationJson = "{\"domain\":\"example.onmicrosoft.com\"}") => new(
        null,
        name,
        EnterpriseProviderKind.MicrosoftEntraId,
        "tenant-123",
        "https://login.microsoftonline.com/tenant-123/v2.0",
        "client-123",
        "https://graph.microsoft.com/v1.0",
        configurationJson,
        "env:ENTRA_SECRET",
        null,
        LoginEnabled: true,
        ProvisioningEnabled: true,
        IsEnabled: true);

    private static GitCandyEnterpriseExternalIdentity NewIdentity(long connectionId, string externalId) => new()
    {
        ConnectionId = connectionId,
        ExternalId = externalId,
        UserName = externalId,
        NormalizedUserName = externalId.ToUpperInvariant(),
        IsActive = true,
        FirstSeenAtUtc = DateTime.UtcNow,
        LastSeenAtUtc = DateTime.UtcNow
    };

    private sealed class EnterpriseFixture : IAsyncDisposable
    {
        public const string TeamName = "enterprise";
        public const string OwnerId = "owner-id";
        public const string LeaderId = "leader-id";
        private readonly SqliteConnection _connection;

        private EnterpriseFixture(ServiceProvider services, SqliteConnection connection)
        {
            Services = services;
            _connection = connection;
        }

        public ServiceProvider Services { get; }

        public static async Task<EnterpriseFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddDbContext<GitCandyDbContext>(options => options.UseSqlite(connection));
            services.AddSingleton<IEnterpriseSecretResolver>(new FixedSecretResolver());
            services.AddSingleton<IEnterpriseProvider>(new FixedEnterpriseProvider());
            services.AddGitCandyApplicationServices();
            var provider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Users.AddRange(NewUser(OwnerId, "owner"), NewUser(LeaderId, "leader"));
            var team = new GitCandyTeam
            {
                Name = TeamName,
                DisplayName = "Enterprise",
                Description = string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
            team.UserRoles.Add(new GitCandyUserTeamRole { UserId = OwnerId, Role = TeamRole.TeamOwner });
            team.UserRoles.Add(new GitCandyUserTeamRole { UserId = LeaderId, Role = TeamRole.Leader });
            dbContext.Teams.Add(team);
            await dbContext.SaveChangesAsync();
            return new EnterpriseFixture(provider, connection);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private static GitCandyUser NewUser(string id, string userName) => new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant()
        };
    }

    private sealed class FixedSecretResolver : IEnterpriseSecretResolver
    {
        public ValueTask<EnterpriseSecret?> ResolveAsync(
            string secretReference,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<EnterpriseSecret?>(new("resolved-value"));
    }

    private sealed class FixedEnterpriseProvider : IEnterpriseProvider
    {
        public EnterpriseProviderKind Kind => EnterpriseProviderKind.MicrosoftEntraId;
        public EnterpriseProviderCapabilities Capabilities =>
            EnterpriseProviderCapabilities.Login | EnterpriseProviderCapabilities.DirectoryUsers;

        public Task<EnterpriseProviderDiagnostic> TestAsync(
            EnterpriseConnectionContext connection,
            EnterpriseSecret secret,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual("resolved-value", secret.Value);
            return Task.FromResult(new EnterpriseProviderDiagnostic(true, "connected", "Connection succeeded."));
        }
    }
}
