using GitCandy.Data.Configuration;
using GitCandy.Data.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class GitCandySqlServerMigrationTests
{
    [TestMethod]
    public async Task SqlServerMigration_WithInitialSchema_GeneratesIdentityAndDomainSql()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "sqlserver",
                ["ConnectionStrings:GitCandy"] =
                    "Server=(localdb)\\mssqllocaldb;Database=GitCandyMigrationSql;Trusted_Connection=True;TrustServerCertificate=True"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddGitCandyData(configuration, builder => builder.AddSqlServer());

        await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<GitCandyDbContext>>();
        await using var dbContext = await factory.CreateDbContextAsync();

        Assert.IsTrue(
            dbContext.Database.GetMigrations().Any(static migration =>
                migration.EndsWith("_InitialIdentitySchema", StringComparison.Ordinal)),
            "SQL Server migrations assembly must contain the initial schema migration.");

        var migrator = dbContext.GetService<IMigrator>();
        var migrationSql = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        StringAssert.Contains(migrationSql, "CREATE TABLE [AspNetUsers]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [AspNetRoles]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [Repositories]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [Teams]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [SshKeys]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [UserRepositoryRoles]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [TeamRepositoryRoles]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [UserTeamRoles]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [Namespaces]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [NamespaceAliases]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [RepositoryAliases]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [NamespaceClaims]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [RepositoryClaims]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [RenameEvents]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [LegacyRepositoryRoutes]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [WorkItemSequences]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [Issues]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [IssueComments]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [IssueTimelineEvents]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [IssueNotifications]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [PullRequests]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [PullRequestTimelineEvents]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [Todos]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [Notifications]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [ActivityEvents]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [RepositoryStars]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [RepositoryInteractions]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [RepositoryMetricsDaily]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [RepositoryPageViews]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [RepositoryRecommendationSnapshots]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [PersonalAccessTokens]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [DeployKeys]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [SshFingerprintClaims]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [BranchProtectionRules]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [CredentialAuditEvents]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [GovernanceAuditEvents]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [BranchProtectionRequiredChecks]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [CommitChecks]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [IntegrationEvents]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [WebhookSubscriptions]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [WebhookDeliveries]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [NotificationPreferences]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [NotificationDeliveries]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [Releases]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [ReleaseAssets]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [RemoteAccountConnections]");
        StringAssert.Contains(migrationSql, "CREATE TABLE [RepositoryMirrors]");
        StringAssert.Contains(migrationSql, "[ServerUrl] nvarchar(512) NOT NULL");
        StringAssert.Contains(migrationSql, "[EventType] nvarchar(24) NOT NULL DEFAULT N'Issue'");
        StringAssert.Contains(migrationSql, "[RequiredApprovals] int NOT NULL");
        StringAssert.Contains(migrationSql, "[RequireCodeOwnerReviews] bit NOT NULL");
        StringAssert.Contains(migrationSql, "[DismissStaleApprovals] bit NOT NULL");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_Issues_RepositoryId_Number]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_PullRequests_RepositoryId_Number]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_PullRequests_RepositoryId_ActivePairKey]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_Todos_User_Kind_Resource]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_Notifications_User_Event]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_RecommendationSnapshots_Snapshot_Repository]");
        StringAssert.Contains(migrationSql, "[Id] bigint NOT NULL IDENTITY");
        StringAssert.Contains(migrationSql, "[CreatedAtUtc] datetime2 NOT NULL");
        StringAssert.Contains(migrationSql, "[IsPrivate] bit NOT NULL");
        StringAssert.Contains(migrationSql, "[Fingerprint] nchar(47) NOT NULL");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_Repositories_NamespaceId_NormalizedName]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_Namespaces_NormalizedSlug]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_Teams_NormalizedName]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_SshKeys_Fingerprint]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_DeployKeys_Fingerprint]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_PersonalAccessTokens_TokenHash]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_BranchProtectionRules_RepositoryId_Pattern]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_CommitChecks_Repository_Sha_Kind_Context]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_WebhookSubscriptions_RepositoryId_Name]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_Releases_Repository_Tag]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_RemoteAccountConnections_StableIdentity]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_RepositoryMirrors_Target_Direction]");
        StringAssert.Contains(migrationSql, "CK_RemoteAccountConnections_Owner");
        StringAssert.Contains(migrationSql, "CK_RepositoryMirrors_DirectionAuthority");
        StringAssert.Contains(migrationSql, "CK_RepositoryMirrors_ScheduleInterval");
        StringAssert.Contains(migrationSql, "CK_RepositoryMirrors_ScheduleConfiguration");
        StringAssert.Contains(migrationSql, "CK_RepositoryMirrors_RefFilter");
        StringAssert.Contains(migrationSql, "[Role] nvarchar(20) NOT NULL DEFAULT N'Member'");
        StringAssert.Contains(
            migrationSql,
            "UPDATE UserTeamRoles SET Role = 'TeamOwner' WHERE IsAdministrator = 1");
        StringAssert.Contains(migrationSql, "CK_UserTeamRoles_Role");
        StringAssert.Contains(migrationSql, "IX_UserTeamRoles_TeamId_Role");
        Assert.IsFalse(migrationSql.Contains("CREATE TABLE [Users]", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(migrationSql.Contains("AuthorizationLog", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(migrationSql.Contains("PasswordVersion", StringComparison.OrdinalIgnoreCase));
    }
}
