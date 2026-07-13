using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SonnetDB.Migrations
{
    /// <inheritdoc />
    public partial class MachineCredentialsAndPushGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BranchProtectionRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    Pattern = table.Column<string>(type: "STRING", maxLength: 255, nullable: false),
                    PushAccess = table.Column<int>(type: "INT", nullable: false),
                    MergeAccess = table.Column<int>(type: "INT", nullable: false),
                    AllowForcePushes = table.Column<bool>(type: "BOOL", nullable: false),
                    AllowDeletions = table.Column<bool>(type: "BOOL", nullable: false),
                    AllowAdministratorBypass = table.Column<bool>(type: "BOOL", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchProtectionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BranchProtectionRules_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CredentialAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    CredentialKind = table.Column<string>(type: "STRING", maxLength: 32, nullable: false),
                    CredentialId = table.Column<long>(type: "INT", nullable: false),
                    ActorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    RepositoryId = table.Column<long>(type: "INT", nullable: true),
                    Action = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "STRING", maxLength: 32, nullable: false),
                    Detail = table.Column<string>(type: "STRING", maxLength: 1000, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredentialAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeployKeys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    Name = table.Column<string>(type: "STRING", maxLength: 100, nullable: false),
                    KeyType = table.Column<string>(type: "STRING", maxLength: 20, nullable: false),
                    Fingerprint = table.Column<string>(type: "STRING", fixedLength: true, maxLength: 47, nullable: false),
                    PublicKey = table.Column<string>(type: "STRING", maxLength: 600, nullable: false),
                    CanWrite = table.Column<bool>(type: "BOOL", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    LastUsedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeployKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeployKeys_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    ActorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    DeployKeyId = table.Column<long>(type: "INT", nullable: true),
                    Action = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "STRING", maxLength: 32, nullable: false),
                    ReferenceName = table.Column<string>(type: "STRING", maxLength: 255, nullable: false),
                    Detail = table.Column<string>(type: "STRING", maxLength: 1000, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PersonalAccessTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "STRING", maxLength: 100, nullable: false),
                    TokenHash = table.Column<string>(type: "STRING", fixedLength: true, maxLength: 64, nullable: false),
                    TokenPrefix = table.Column<string>(type: "STRING", maxLength: 16, nullable: false),
                    Scopes = table.Column<string>(type: "STRING", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    LastUsedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalAccessTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonalAccessTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SshFingerprintClaims",
                columns: table => new
                {
                    Fingerprint = table.Column<string>(type: "STRING", fixedLength: true, maxLength: 47, nullable: false),
                    CredentialKind = table.Column<string>(type: "STRING", maxLength: 32, nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SshFingerprintClaims", x => x.Fingerprint);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BranchProtectionRules_RepositoryId_Pattern",
                table: "BranchProtectionRules",
                columns: new[] { "RepositoryId", "Pattern" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CredentialAuditEvents_Credential_OccurredAtUtc",
                table: "CredentialAuditEvents",
                columns: new[] { "CredentialKind", "CredentialId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CredentialAuditEvents_RepositoryId_OccurredAtUtc",
                table: "CredentialAuditEvents",
                columns: new[] { "RepositoryId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeployKeys_Fingerprint",
                table: "DeployKeys",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeployKeys_RepositoryId",
                table: "DeployKeys",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceAuditEvents_RepositoryId_OccurredAtUtc",
                table: "GovernanceAuditEvents",
                columns: new[] { "RepositoryId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_TokenHash",
                table: "PersonalAccessTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_UserId_RevokedAtUtc",
                table: "PersonalAccessTokens",
                columns: new[] { "UserId", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BranchProtectionRules");

            migrationBuilder.DropTable(
                name: "CredentialAuditEvents");

            migrationBuilder.DropTable(
                name: "DeployKeys");

            migrationBuilder.DropTable(
                name: "GovernanceAuditEvents");

            migrationBuilder.DropTable(
                name: "PersonalAccessTokens");

            migrationBuilder.DropTable(
                name: "SshFingerprintClaims");
        }
    }
}
