using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Remotes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class RemoteMirrorSchemaTests
{
    [TestMethod]
    public async Task RemoteMirrorSchema_WithValidPullMirror_PersistsCredentialReferenceAndPolicies()
    {
        await using var fixture = await RemoteMirrorFixture.CreateAsync();
        var dbContext = fixture.DbContext;
        var user = await fixture.CreateUserAsync("remote-owner");
        var repository = await fixture.CreateRepositoryAsync("remote-schema");
        var connection = CreateConnection(user.Id);
        dbContext.RemoteAccountConnections.Add(connection);
        await dbContext.SaveChangesAsync();

        dbContext.RepositoryMirrors.Add(new GitCandyRepositoryMirror
        {
            RepositoryId = repository.Id,
            ConnectionId = connection.Id,
            RemoteRepositoryId = "R_kgDOStable",
            RemoteOwnerLogin = "octo-org",
            RemoteRepositoryName = "renamed-upstream",
            RemoteGitUrl = "https://github.com/octo-org/renamed-upstream.git",
            Direction = RemoteMirrorDirection.Pull,
            Authority = RemoteMirrorAuthority.Remote,
            RefFilterKind = RemoteMirrorRefFilterKind.AllowList,
            RefFilterPattern = "refs/heads/main\nrefs/tags/v*",
            ScheduleIntervalMinutes = 15,
            ScheduleTimeZone = "UTC",
            ScheduleEnabled = true,
            DivergencePolicy = RemoteMirrorDivergencePolicy.Stop,
            Prune = false,
            IsEnabled = true,
            Status = RemoteMirrorStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var saved = await dbContext.RepositoryMirrors
            .Include(item => item.Connection)
            .SingleAsync();
        Assert.AreEqual(RemoteMirrorDirection.Pull, saved.Direction);
        Assert.AreEqual(RemoteMirrorAuthority.Remote, saved.Authority);
        Assert.AreEqual(RemoteMirrorDivergencePolicy.Stop, saved.DivergencePolicy);
        Assert.AreEqual("R_kgDOStable", saved.RemoteRepositoryId);
        Assert.IsNotNull(saved.Connection);
        Assert.AreEqual("vault:remote/user-1", saved.Connection.CredentialReference);
        Assert.AreEqual("[\"repo:read\"]", saved.Connection.GrantedScopes);
        Assert.IsFalse(
            dbContext.Model.FindEntityType(typeof(GitCandyRemoteAccountConnection))!
                .GetProperties()
                .Any(property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)),
            "Remote connection schema must not expose plaintext token or password columns.");
    }

    [TestMethod]
    public async Task RemoteMirrorSchema_WithMismatchedDirectionAuthority_RejectsRow()
    {
        await using var fixture = await RemoteMirrorFixture.CreateAsync();
        var user = await fixture.CreateUserAsync("invalid-mirror-owner");
        var repository = await fixture.CreateRepositoryAsync("invalid-mirror");
        var connection = CreateConnection(user.Id);
        fixture.DbContext.RemoteAccountConnections.Add(connection);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.RepositoryMirrors.Add(new GitCandyRepositoryMirror
        {
            RepositoryId = repository.Id,
            ConnectionId = connection.Id,
            RemoteRepositoryId = "repository-2",
            RemoteOwnerLogin = "octo-org",
            RemoteRepositoryName = "invalid-mirror",
            RemoteGitUrl = "https://github.com/octo-org/invalid-mirror.git",
            Direction = RemoteMirrorDirection.Pull,
            Authority = RemoteMirrorAuthority.GitCandy,
            RefFilterKind = RemoteMirrorRefFilterKind.AllRefs,
            DivergencePolicy = RemoteMirrorDivergencePolicy.Stop,
            IsEnabled = true,
            Status = RemoteMirrorStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(
            () => fixture.DbContext.SaveChangesAsync());
    }

    [TestMethod]
    public async Task RemoteAccountSchema_WithBothOwners_RejectsAmbiguousOwnership()
    {
        await using var fixture = await RemoteMirrorFixture.CreateAsync();
        var user = await fixture.CreateUserAsync("ambiguous-owner");
        var team = new GitCandyTeam
        {
            Name = "remote-team",
            DisplayName = "Remote team",
            Description = string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };
        fixture.DbContext.Teams.Add(team);
        await fixture.DbContext.SaveChangesAsync();
        var connection = CreateConnection(user.Id);
        connection.OwnerTeamId = team.Id;
        fixture.DbContext.RemoteAccountConnections.Add(connection);

        await Assert.ThrowsExactlyAsync<DbUpdateException>(
            () => fixture.DbContext.SaveChangesAsync());
    }

    [TestMethod]
    public async Task RemoteMirrorSchema_WithEnabledScheduleWithoutTimeZone_RejectsRow()
    {
        await using var fixture = await RemoteMirrorFixture.CreateAsync();
        var user = await fixture.CreateUserAsync("schedule-owner");
        var repository = await fixture.CreateRepositoryAsync("invalid-schedule");
        var connection = CreateConnection(user.Id);
        fixture.DbContext.RemoteAccountConnections.Add(connection);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.RepositoryMirrors.Add(new GitCandyRepositoryMirror
        {
            RepositoryId = repository.Id,
            ConnectionId = connection.Id,
            RemoteRepositoryId = "repository-schedule",
            RemoteOwnerLogin = "octo-org",
            RemoteRepositoryName = "invalid-schedule",
            RemoteGitUrl = "https://github.com/octo-org/invalid-schedule.git",
            Direction = RemoteMirrorDirection.Pull,
            Authority = RemoteMirrorAuthority.Remote,
            RefFilterKind = RemoteMirrorRefFilterKind.AllRefs,
            ScheduleIntervalMinutes = 15,
            ScheduleEnabled = true,
            DivergencePolicy = RemoteMirrorDivergencePolicy.Stop,
            IsEnabled = true,
            Status = RemoteMirrorStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await Assert.ThrowsExactlyAsync<DbUpdateException>(
            () => fixture.DbContext.SaveChangesAsync());
    }

    private static GitCandyRemoteAccountConnection CreateConnection(string userId) => new()
    {
        OwnerKind = RemoteConnectionOwnerKind.User,
        OwnerUserId = userId,
        Provider = RemoteProviderKind.GitHub,
        ServerUrl = "https://github.com/",
        ExternalAccountId = "account-1",
        AccountKind = RemoteAccountKind.User,
        Login = "octocat",
        DisplayName = "Octocat",
        AuthenticationKind = RemoteAuthenticationKind.App,
        CredentialReference = "vault:remote/user-1",
        GrantedScopes = "[\"repo:read\"]",
        IsEnabled = true,
        Status = RemoteConnectionStatus.NotTested,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private sealed class RemoteMirrorFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private RemoteMirrorFixture(SqliteConnection connection, GitCandyDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
        }

        public GitCandyDbContext DbContext { get; }

        public static async Task<RemoteMirrorFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<GitCandyDbContext>()
                .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly("GitCandy.Data.Sqlite"))
                .Options;
            var dbContext = new GitCandyDbContext(options);
            await dbContext.Database.MigrateAsync();
            return new RemoteMirrorFixture(connection, dbContext);
        }

        public async Task<GitCandyUser> CreateUserAsync(string userName)
        {
            var user = new GitCandyUser
            {
                Id = Guid.NewGuid().ToString("N"),
                UserName = userName,
                NormalizedUserName = userName.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            DbContext.Users.Add(user);
            await DbContext.SaveChangesAsync();
            return user;
        }

        public async Task<GitCandyRepository> CreateRepositoryAsync(string name)
        {
            var repository = new GitCandyRepository
            {
                NamespaceId = GitCandyNamespace.LegacyNamespaceId,
                StorageName = name,
                Name = name,
                Description = string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
            DbContext.Repositories.Add(repository);
            await DbContext.SaveChangesAsync();
            return repository;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
