using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SqlServer.Migrations
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExternalOrganizationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Authority = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ClientId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApiBaseUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: true),
                    SecretReference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SyncCursor = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LoginEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ProvisioningEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    LastErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastTestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSynchronizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectionId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeprovisionedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
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
