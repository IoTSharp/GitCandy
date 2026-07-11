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
        StringAssert.Contains(migrationSql, "[Id] bigint NOT NULL IDENTITY");
        StringAssert.Contains(migrationSql, "[CreatedAtUtc] datetime2 NOT NULL");
        StringAssert.Contains(migrationSql, "[IsPrivate] bit NOT NULL");
        StringAssert.Contains(migrationSql, "[Fingerprint] nchar(47) NOT NULL");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_Repositories_NamespaceId_NormalizedName]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_Namespaces_NormalizedSlug]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_Teams_NormalizedName]");
        StringAssert.Contains(migrationSql, "CREATE UNIQUE INDEX [IX_SshKeys_Fingerprint]");
        Assert.IsFalse(migrationSql.Contains("CREATE TABLE [Users]", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(migrationSql.Contains("AuthorizationLog", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(migrationSql.Contains("PasswordVersion", StringComparison.OrdinalIgnoreCase));
    }
}
