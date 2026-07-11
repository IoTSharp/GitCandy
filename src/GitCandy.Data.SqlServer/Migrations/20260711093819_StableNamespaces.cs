using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GitCandy.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class StableNamespaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Repositories_NormalizedName",
                table: "Repositories");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "Teams",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "NamespaceId",
                table: "Repositories",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "StorageName",
                table: "Repositories",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "LegacyRepositoryRoutes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Project = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NormalizedProject = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyRepositoryRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegacyRepositoryRoutes_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Namespaces",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TeamId = table.Column<long>(type: "bigint", nullable: true),
                    Slug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NormalizedSlug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Namespaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Namespaces_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Namespaces_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RenameEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    SubjectType = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<long>(type: "bigint", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OldSlug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NewSlug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsOverride = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenameEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NamespaceAliases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NamespaceId = table.Column<long>(type: "bigint", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NormalizedSlug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NamespaceAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NamespaceAliases_Namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalTable: "Namespaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryAliases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NamespaceId = table.Column<long>(type: "bigint", nullable: false),
                    RepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NormalizedSlug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoryAliases_Namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalTable: "Namespaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepositoryAliases_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NamespaceClaims",
                columns: table => new
                {
                    NormalizedSlug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClaimType = table.Column<int>(type: "int", nullable: false),
                    NamespaceId = table.Column<long>(type: "bigint", nullable: true),
                    NamespaceAliasId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NamespaceClaims", x => x.NormalizedSlug);
                    table.ForeignKey(
                        name: "FK_NamespaceClaims_NamespaceAliases_NamespaceAliasId",
                        column: x => x.NamespaceAliasId,
                        principalTable: "NamespaceAliases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NamespaceClaims_Namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalTable: "Namespaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryClaims",
                columns: table => new
                {
                    NamespaceId = table.Column<long>(type: "bigint", nullable: false),
                    NormalizedSlug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClaimType = table.Column<int>(type: "int", nullable: false),
                    RepositoryId = table.Column<long>(type: "bigint", nullable: true),
                    RepositoryAliasId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryClaims", x => new { x.NamespaceId, x.NormalizedSlug });
                    table.ForeignKey(
                        name: "FK_RepositoryClaims_Namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalTable: "Namespaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepositoryClaims_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepositoryClaims_RepositoryAliases_RepositoryAliasId",
                        column: x => x.RepositoryAliasId,
                        principalTable: "RepositoryAliases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "NamespaceClaims",
                columns: new[] { "NormalizedSlug", "ClaimType", "NamespaceAliasId", "NamespaceId", "Slug" },
                values: new object?[,]
                {
                    { "ACCOUNT", 3, null, null, "account" },
                    { "API", 3, null, null, "api" },
                    { "ASSETS", 3, null, null, "assets" },
                    { "GIT", 3, null, null, "git" },
                    { "HEALTH", 3, null, null, "health" },
                    { "HOME", 3, null, null, "home" },
                    { "IDENTITY", 3, null, null, "identity" },
                    { "REPOSITORY", 3, null, null, "repository" },
                    { "SETTING", 3, null, null, "setting" },
                    { "SIGNIN-OIDC", 3, null, null, "signin-oidc" },
                    { "SIGNOUT-CALLBACK-OIDC", 3, null, null, "signout-callback-oidc" },
                    { "TEAM", 3, null, null, "team" }
                });

            migrationBuilder.InsertData(
                table: "Namespaces",
                columns: new[] { "Id", "CreatedAtUtc", "NormalizedSlug", "OwnerType", "Slug", "TeamId", "UserId", "Version" },
                values: new object?[] { 1L, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "LEGACY", 0, "legacy", null, null, 0 });

            migrationBuilder.InsertData(
                table: "NamespaceClaims",
                columns: new[] { "NormalizedSlug", "ClaimType", "NamespaceAliasId", "NamespaceId", "Slug" },
                values: new object?[] { "LEGACY", 1, null, 1L, "legacy" });

            migrationBuilder.Sql(
                """
                UPDATE [Teams] SET [DisplayName] = [Name] WHERE [DisplayName] = N'';
                UPDATE [Repositories] SET [StorageName] = [Name] WHERE [StorageName] = N'';

                INSERT INTO [Namespaces] ([OwnerType], [UserId], [TeamId], [Slug], [NormalizedSlug], [CreatedAtUtc], [Version])
                SELECT 1, [Id], NULL, [UserName], [NormalizedUserName], SYSUTCDATETIME(), 0
                FROM [AspNetUsers]
                WHERE [UserName] IS NOT NULL AND [NormalizedUserName] IS NOT NULL;

                INSERT INTO [Namespaces] ([OwnerType], [UserId], [TeamId], [Slug], [NormalizedSlug], [CreatedAtUtc], [Version])
                SELECT 2, NULL, [Id], [Name], [NormalizedName], [CreatedAtUtc], 0
                FROM [Teams];

                INSERT INTO [NamespaceClaims] ([NormalizedSlug], [Slug], [ClaimType], [NamespaceId], [NamespaceAliasId])
                SELECT [NormalizedSlug], [Slug], 1, [Id], NULL
                FROM [Namespaces]
                WHERE [Id] <> 1;

                UPDATE repository
                SET [NamespaceId] = COALESCE(ownerNamespace.[Id], 1)
                FROM [Repositories] repository
                OUTER APPLY (
                    SELECT TOP (1) namespaceItem.[Id]
                    FROM [UserRepositoryRoles] ownerRole
                    INNER JOIN [Namespaces] namespaceItem ON namespaceItem.[UserId] = ownerRole.[UserId]
                    WHERE ownerRole.[RepositoryId] = repository.[Id] AND ownerRole.[IsOwner] = 1
                    ORDER BY ownerRole.[UserId]
                ) ownerNamespace;

                INSERT INTO [RepositoryClaims] ([NamespaceId], [NormalizedSlug], [Slug], [ClaimType], [RepositoryId], [RepositoryAliasId])
                SELECT [NamespaceId], [NormalizedName], [Name], 1, [Id], NULL
                FROM [Repositories];

                INSERT INTO [LegacyRepositoryRoutes] ([Project], [NormalizedProject], [RepositoryId], [CreatedAtUtc])
                SELECT [Name], [NormalizedName], [Id], [CreatedAtUtc]
                FROM [Repositories];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_NamespaceId_NormalizedName",
                table: "Repositories",
                columns: new[] { "NamespaceId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_StorageName",
                table: "Repositories",
                column: "StorageName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyRepositoryRoutes_NormalizedProject",
                table: "LegacyRepositoryRoutes",
                column: "NormalizedProject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyRepositoryRoutes_RepositoryId",
                table: "LegacyRepositoryRoutes",
                column: "RepositoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceAliases_ExpiresAtUtc",
                table: "NamespaceAliases",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceAliases_NamespaceId_CreatedAtUtc",
                table: "NamespaceAliases",
                columns: new[] { "NamespaceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceClaims_NamespaceAliasId",
                table: "NamespaceClaims",
                column: "NamespaceAliasId",
                unique: true,
                filter: "[NamespaceAliasId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceClaims_NamespaceId",
                table: "NamespaceClaims",
                column: "NamespaceId",
                unique: true,
                filter: "[NamespaceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Namespaces_NormalizedSlug",
                table: "Namespaces",
                column: "NormalizedSlug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Namespaces_TeamId",
                table: "Namespaces",
                column: "TeamId",
                unique: true,
                filter: "[TeamId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Namespaces_UserId",
                table: "Namespaces",
                column: "UserId",
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RenameEvents_Subject_Window",
                table: "RenameEvents",
                columns: new[] { "SubjectType", "SubjectId", "EventType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAliases_ExpiresAtUtc",
                table: "RepositoryAliases",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAliases_NamespaceId",
                table: "RepositoryAliases",
                column: "NamespaceId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAliases_RepositoryId_CreatedAtUtc",
                table: "RepositoryAliases",
                columns: new[] { "RepositoryId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryClaims_RepositoryAliasId",
                table: "RepositoryClaims",
                column: "RepositoryAliasId",
                unique: true,
                filter: "[RepositoryAliasId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryClaims_RepositoryId",
                table: "RepositoryClaims",
                column: "RepositoryId",
                unique: true,
                filter: "[RepositoryId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Repositories_Namespaces_NamespaceId",
                table: "Repositories",
                column: "NamespaceId",
                principalTable: "Namespaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Repositories_Namespaces_NamespaceId",
                table: "Repositories");

            migrationBuilder.DropTable(
                name: "LegacyRepositoryRoutes");

            migrationBuilder.DropTable(
                name: "NamespaceClaims");

            migrationBuilder.DropTable(
                name: "RenameEvents");

            migrationBuilder.DropTable(
                name: "RepositoryClaims");

            migrationBuilder.DropTable(
                name: "NamespaceAliases");

            migrationBuilder.DropTable(
                name: "RepositoryAliases");

            migrationBuilder.DropTable(
                name: "Namespaces");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_NamespaceId_NormalizedName",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_StorageName",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "NamespaceId",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "StorageName",
                table: "Repositories");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_NormalizedName",
                table: "Repositories",
                column: "NormalizedName",
                unique: true);
        }
    }
}
