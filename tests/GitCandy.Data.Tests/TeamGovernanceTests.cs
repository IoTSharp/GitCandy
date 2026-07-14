using GitCandy.Application;
using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Teams;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class TeamGovernanceTests
{
    [TestMethod]
    public void Allows_WithFourLevelRoles_EnforcesGovernanceMatrix()
    {
        Assert.IsTrue(Enum.GetValues<TeamPermission>().All(
            permission => TeamRolePermissions.Allows(TeamRole.TeamOwner, permission)));

        Assert.IsTrue(TeamRolePermissions.Allows(
            TeamRole.Leader,
            TeamPermission.ViewEnterpriseConnections));
        Assert.IsTrue(TeamRolePermissions.Allows(
            TeamRole.Leader,
            TeamPermission.ManageDeputyLeaders));
        Assert.IsTrue(TeamRolePermissions.Allows(
            TeamRole.Leader,
            TeamPermission.CreateTeamRepository));
        Assert.IsFalse(TeamRolePermissions.Allows(
            TeamRole.Leader,
            TeamPermission.ManageEnterpriseConnections));
        Assert.IsFalse(TeamRolePermissions.Allows(
            TeamRole.Leader,
            TeamPermission.RenameTeam));
        Assert.IsFalse(TeamRolePermissions.Allows(
            TeamRole.Leader,
            TeamPermission.ManageLeaders));

        Assert.IsTrue(TeamRolePermissions.Allows(
            TeamRole.DeputyLeader,
            TeamPermission.ManageMembers));
        Assert.AreEqual(
            1,
            Enum.GetValues<TeamPermission>().Count(
                permission => TeamRolePermissions.Allows(TeamRole.DeputyLeader, permission)));
        Assert.IsFalse(Enum.GetValues<TeamPermission>().Any(
            permission => TeamRolePermissions.Allows(TeamRole.Member, permission)));
    }

    [TestMethod]
    public void CanManage_WithTargetRoles_RestrictsDelegationHierarchy()
    {
        Assert.IsTrue(TeamRolePermissions.CanManage(TeamRole.TeamOwner, TeamRole.TeamOwner));
        Assert.IsTrue(TeamRolePermissions.CanManage(TeamRole.Leader, TeamRole.DeputyLeader));
        Assert.IsTrue(TeamRolePermissions.CanManage(TeamRole.DeputyLeader, TeamRole.Member));
        Assert.IsFalse(TeamRolePermissions.CanManage(TeamRole.Leader, TeamRole.Leader));
        Assert.IsFalse(TeamRolePermissions.CanManage(TeamRole.DeputyLeader, TeamRole.DeputyLeader));
        Assert.IsFalse(TeamRolePermissions.CanManage(TeamRole.Member, TeamRole.Member));
    }

    [TestMethod]
    public async Task MigrateAsync_WithLegacyAdministratorFlags_BackfillsFourLevelRoles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GitCandyDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly("GitCandy.Data.Sqlite"))
            .Options;
        await using var dbContext = new GitCandyDbContext(options);
        var migrator = dbContext.GetService<IMigrator>();
        var previousMigration = dbContext.Database.GetMigrations().Single(
            migration => migration.EndsWith("_M13CollaborationExtensions", StringComparison.Ordinal));
        await migrator.MigrateAsync(previousMigration);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO AspNetUsers
                (Id, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
            VALUES
                ('legacy-owner', 0, 0, 0, 0, 0),
                ('legacy-member', 0, 0, 0, 0, 0)
            """);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Teams (Id, Name, DisplayName, NormalizedName, Description, CreatedAtUtc)
            VALUES (1, 'core', 'Core', 'CORE', 'Core team', '2026-07-13 00:00:00')
            """);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO UserTeamRoles (UserId, TeamId, IsAdministrator)
            VALUES ('legacy-owner', 1, 1), ('legacy-member', 1, 0)
            """);

        await dbContext.Database.MigrateAsync();
        dbContext.ChangeTracker.Clear();

        var roles = await dbContext.UserTeamRoles
            .OrderBy(role => role.UserId)
            .ToArrayAsync();
        Assert.AreEqual(TeamRole.Member, roles[0].Role);
        Assert.AreEqual(TeamRole.TeamOwner, roles[1].Role);
        await Assert.ThrowsExactlyAsync<SqliteException>(() =>
            dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE UserTeamRoles SET Role = 'InvalidRole' WHERE UserId = 'legacy-member'"));
    }

    [TestMethod]
    public async Task SetMemberAsync_WithLastTeamOwnerRemovalOrDemotion_RejectsBothChanges()
    {
        await using var fixture = await TeamFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeamService>();

        Assert.IsFalse(await service.SetMemberAsync(
            TeamFixture.TeamName,
            TeamFixture.OwnerName,
            TeamMemberAction.Remove,
            TeamFixture.OwnerId,
            actorIsSystemAdministrator: false));
        Assert.IsFalse(await service.SetMemberAsync(
            TeamFixture.TeamName,
            TeamFixture.OwnerName,
            TeamMemberAction.MakeLeader,
            TeamFixture.OwnerId,
            actorIsSystemAdministrator: false));

        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var ownerRole = await dbContext.UserTeamRoles.SingleAsync(
            role => role.UserId == TeamFixture.OwnerId);
        Assert.AreEqual(TeamRole.TeamOwner, ownerRole.Role);
    }

    [TestMethod]
    public async Task SetMemberAsync_WithSecondTeamOwner_AllowsFirstOwnerDemotion()
    {
        await using var fixture = await TeamFixture.CreateAsync(includeSecondOwner: true);
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeamService>();

        Assert.IsTrue(await service.SetMemberAsync(
            TeamFixture.TeamName,
            TeamFixture.OwnerName,
            TeamMemberAction.MakeDeputyLeader,
            TeamFixture.OwnerId,
            actorIsSystemAdministrator: false));

        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var roles = await dbContext.UserTeamRoles
            .Where(role => role.TeamId == fixture.TeamId)
            .ToDictionaryAsync(role => role.UserId, role => role.Role);
        Assert.AreEqual(TeamRole.DeputyLeader, roles[TeamFixture.OwnerId]);
        Assert.AreEqual(TeamRole.TeamOwner, roles[TeamFixture.SecondOwnerId]);
    }

    [TestMethod]
    public async Task IsAllowedAsync_WithFourRoles_AppliesPermissionMatrixFromDatabase()
    {
        await using var fixture = await TeamFixture.CreateAsync(includeGovernanceRoles: true);
        await using var scope = fixture.Services.CreateAsyncScope();
        var authorization = scope.ServiceProvider.GetRequiredService<ITeamAuthorizationService>();

        Assert.IsTrue(await authorization.IsAllowedAsync(
            TeamFixture.TeamName,
            TeamFixture.LeaderId,
            false,
            TeamPermission.ManageMembers));
        Assert.IsFalse(await authorization.IsAllowedAsync(
            TeamFixture.TeamName,
            TeamFixture.LeaderId,
            false,
            TeamPermission.ManageEnterpriseConnections));
        Assert.IsTrue(await authorization.IsAllowedAsync(
            TeamFixture.TeamName,
            TeamFixture.DeputyId,
            false,
            TeamPermission.ManageMembers));
        Assert.IsFalse(await authorization.IsAllowedAsync(
            TeamFixture.TeamName,
            TeamFixture.MemberId,
            false,
            TeamPermission.ManageMembers));
    }

    [TestMethod]
    public async Task ApplyMemberChangesAsync_WithLeaderBatch_UpdatesAtomicallyAndAuditsEachMember()
    {
        await using var fixture = await TeamFixture.CreateAsync(includeGovernanceRoles: true);
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeamService>();

        var result = await service.ApplyMemberChangesAsync(
            TeamFixture.TeamName,
            [
                new TeamMemberChange("deputy", TeamMemberAction.MakeMember),
                new TeamMemberChange("member", TeamMemberAction.MakeDeputyLeader)
            ],
            TeamFixture.LeaderId,
            actorIsSystemAdministrator: false);

        Assert.IsTrue(result.Succeeded, result.Error);
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var roles = await dbContext.UserTeamRoles
            .Where(role => role.TeamId == fixture.TeamId)
            .ToDictionaryAsync(role => role.UserId, role => role.Role);
        Assert.AreEqual(TeamRole.Member, roles[TeamFixture.DeputyId]);
        Assert.AreEqual(TeamRole.DeputyLeader, roles[TeamFixture.MemberId]);
        var audit = await service.GetAuditEventsAsync(TeamFixture.TeamName);
        Assert.AreEqual(2, audit.Count(item => item.Outcome == "succeeded"));
        Assert.IsTrue(audit.All(item => item.Actor == "leader"));
    }

    [TestMethod]
    public async Task ApplyMemberChangesAsync_WithUnchangedRole_DoesNotWriteAuditEvent()
    {
        await using var fixture = await TeamFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeamService>();

        var result = await service.ApplyMemberChangesAsync(
            TeamFixture.TeamName,
            [new TeamMemberChange(TeamFixture.OwnerName, TeamMemberAction.MakeTeamOwner)],
            TeamFixture.OwnerId,
            actorIsSystemAdministrator: false);

        Assert.IsTrue(result.Succeeded, result.Error);
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        Assert.AreEqual(
            TeamRole.TeamOwner,
            (await dbContext.UserTeamRoles.SingleAsync(role => role.UserId == TeamFixture.OwnerId)).Role);
        Assert.AreEqual(0, (await service.GetAuditEventsAsync(TeamFixture.TeamName)).Count);
    }

    [TestMethod]
    public async Task ApplyMemberChangesAsync_WithLastOwnerBatch_RejectsWholeBatchAndRecordsReason()
    {
        await using var fixture = await TeamFixture.CreateAsync(includeGovernanceRoles: true);
        await using var scope = fixture.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeamService>();

        var result = await service.ApplyMemberChangesAsync(
            TeamFixture.TeamName,
            [
                new TeamMemberChange(TeamFixture.OwnerName, TeamMemberAction.MakeMember),
                new TeamMemberChange("member", TeamMemberAction.MakeDeputyLeader)
            ],
            TeamFixture.OwnerId,
            actorIsSystemAdministrator: false);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error ?? string.Empty, "TeamOwner");
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        Assert.AreEqual(
            TeamRole.Member,
            (await dbContext.UserTeamRoles.SingleAsync(role => role.UserId == TeamFixture.MemberId)).Role);
        var audit = await service.GetAuditEventsAsync(TeamFixture.TeamName);
        Assert.AreEqual(1, audit.Count);
        Assert.AreEqual("rejected", audit[0].Outcome);
    }

    [TestMethod]
    public async Task SetMemberAsync_WithOnlyExternalOwnerRemaining_PreservesLocalBreakGlassOwner()
    {
        await using var fixture = await TeamFixture.CreateAsync(includeSecondOwner: true);
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
        var connection = new GitCandyEnterpriseConnection
        {
            TeamId = fixture.TeamId,
            Name = "directory",
            NormalizedName = "DIRECTORY",
            Provider = GitCandy.Enterprise.EnterpriseProviderKind.Scim,
            ExternalOrganizationId = "tenant",
            SecretReference = "env:SCIM_SECRET",
            IsEnabled = true,
            ProvisioningEnabled = true,
            Status = GitCandy.Enterprise.EnterpriseConnectionStatus.Healthy,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        dbContext.EnterpriseConnections.Add(connection);
        await dbContext.SaveChangesAsync();
        dbContext.EnterpriseExternalIdentities.Add(new GitCandyEnterpriseExternalIdentity
        {
            ConnectionId = connection.Id,
            ExternalId = "external-owner",
            UserId = TeamFixture.SecondOwnerId,
            UserName = "second",
            NormalizedUserName = "SECOND",
            IsActive = true,
            FirstSeenAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<ITeamService>();

        var changed = await service.SetMemberAsync(
            TeamFixture.TeamName,
            TeamFixture.OwnerName,
            TeamMemberAction.MakeMember,
            TeamFixture.OwnerId,
            actorIsSystemAdministrator: false);

        Assert.IsFalse(changed);
        Assert.AreEqual(
            TeamRole.TeamOwner,
            (await dbContext.UserTeamRoles.SingleAsync(role => role.UserId == TeamFixture.OwnerId)).Role);
    }

    private sealed class TeamFixture : IAsyncDisposable
    {
        public const string TeamName = "core";
        public const string OwnerId = "team-owner";
        public const string OwnerName = "owner";
        public const string SecondOwnerId = "second-owner";
        public const string LeaderId = "team-leader";
        public const string DeputyId = "team-deputy";
        public const string MemberId = "team-member";

        private readonly SqliteConnection _connection;

        private TeamFixture(ServiceProvider services, SqliteConnection connection, long teamId)
        {
            Services = services;
            _connection = connection;
            TeamId = teamId;
        }

        public ServiceProvider Services { get; }
        public long TeamId { get; }

        public static async Task<TeamFixture> CreateAsync(
            bool includeSecondOwner = false,
            bool includeGovernanceRoles = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddDbContext<GitCandyDbContext>(options => options.UseSqlite(connection));
            services.AddGitCandyApplicationServices();
            var provider = services.BuildServiceProvider(validateScopes: true);

            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            var owner = NewUser(OwnerId, OwnerName);
            var secondOwner = NewUser(SecondOwnerId, "second");
            var leader = NewUser(LeaderId, "leader");
            var deputy = NewUser(DeputyId, "deputy");
            var member = NewUser(MemberId, "member");
            dbContext.Users.Add(owner);
            if (includeSecondOwner)
            {
                dbContext.Users.Add(secondOwner);
            }
            if (includeGovernanceRoles)
            {
                dbContext.Users.AddRange(leader, deputy, member);
            }

            var team = new GitCandyTeam
            {
                Name = TeamName,
                NormalizedName = TeamName.ToUpperInvariant(),
                DisplayName = "Core",
                Description = "Core team",
                CreatedAtUtc = DateTime.UtcNow
            };
            team.UserRoles.Add(new GitCandyUserTeamRole
            {
                UserId = owner.Id,
                Role = TeamRole.TeamOwner
            });
            if (includeSecondOwner)
            {
                team.UserRoles.Add(new GitCandyUserTeamRole
                {
                    UserId = secondOwner.Id,
                    Role = TeamRole.TeamOwner
                });
            }
            if (includeGovernanceRoles)
            {
                team.UserRoles.Add(new GitCandyUserTeamRole
                {
                    UserId = leader.Id,
                    Role = TeamRole.Leader
                });
                team.UserRoles.Add(new GitCandyUserTeamRole
                {
                    UserId = deputy.Id,
                    Role = TeamRole.DeputyLeader
                });
                team.UserRoles.Add(new GitCandyUserTeamRole
                {
                    UserId = member.Id,
                    Role = TeamRole.Member
                });
            }

            dbContext.Teams.Add(team);
            await dbContext.SaveChangesAsync();
            return new TeamFixture(provider, connection, team.Id);
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
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N")
        };
    }
}
