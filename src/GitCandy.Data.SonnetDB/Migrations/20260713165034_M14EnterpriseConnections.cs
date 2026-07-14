using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SonnetDB.Migrations
{
    /// <inheritdoc />
    public partial class M14EnterpriseConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnterpriseConnections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    TeamId = table.Column<long>(type: "INT", nullable: false),
                    Name = table.Column<string>(type: "STRING", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "STRING", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "STRING", maxLength: 32, nullable: false),
                    ExternalOrganizationId = table.Column<string>(type: "STRING", maxLength: 256, nullable: false),
                    Authority = table.Column<string>(type: "STRING", maxLength: 2048, nullable: true),
                    ClientId = table.Column<string>(type: "STRING", maxLength: 256, nullable: true),
                    ApiBaseUrl = table.Column<string>(type: "STRING", maxLength: 2048, nullable: true),
                    ConfigurationJson = table.Column<string>(type: "STRING", maxLength: 8192, nullable: true),
                    SecretReference = table.Column<string>(type: "STRING", maxLength: 512, nullable: false),
                    SyncCursor = table.Column<string>(type: "STRING", maxLength: 2048, nullable: true),
                    LoginEnabled = table.Column<bool>(type: "BOOL", nullable: false),
                    ProvisioningEnabled = table.Column<bool>(type: "BOOL", nullable: false),
                    IsEnabled = table.Column<bool>(type: "BOOL", nullable: false),
                    Status = table.Column<string>(type: "STRING", maxLength: 24, nullable: false),
                    LastErrorCode = table.Column<string>(type: "STRING", maxLength: 64, nullable: true),
                    LastTestedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    LastSynchronizedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnterpriseConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnterpriseConnections_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnterpriseExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    ConnectionId = table.Column<long>(type: "INT", nullable: false),
                    ExternalId = table.Column<string>(type: "STRING", maxLength: 256, nullable: false),
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    UserName = table.Column<string>(type: "STRING", maxLength: 128, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "STRING", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "STRING", maxLength: 128, nullable: true),
                    DisplayName = table.Column<string>(type: "STRING", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "BOOL", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    DeprovisionedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnterpriseExternalIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnterpriseExternalIdentities_EnterpriseConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "EnterpriseConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseConnections_Team_Provider_Organization",
                table: "EnterpriseConnections",
                columns: new[] { "TeamId", "Provider", "ExternalOrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseConnections_TeamId_Name",
                table: "EnterpriseConnections",
                columns: new[] { "TeamId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseExternalIdentities_ConnectionId_ExternalId",
                table: "EnterpriseExternalIdentities",
                columns: new[] { "ConnectionId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseExternalIdentities_ConnectionId_UserName",
                table: "EnterpriseExternalIdentities",
                columns: new[] { "ConnectionId", "NormalizedUserName" });

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseExternalIdentities_UserId",
                table: "EnterpriseExternalIdentities",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnterpriseExternalIdentities");

            migrationBuilder.DropTable(
                name: "EnterpriseConnections");
        }
    }
}
