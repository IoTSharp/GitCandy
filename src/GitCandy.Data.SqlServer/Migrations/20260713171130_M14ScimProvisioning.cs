using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class M14ScimProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnterpriseGroups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectionId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnterpriseGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnterpriseGroups_EnterpriseConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "EnterpriseConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnterpriseScimCredentials",
                columns: table => new
                {
                    ConnectionId = table.Column<long>(type: "bigint", nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnterpriseScimCredentials", x => x.ConnectionId);
                    table.ForeignKey(
                        name: "FK_EnterpriseScimCredentials_EnterpriseConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "EnterpriseConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnterpriseGroupMembers",
                columns: table => new
                {
                    GroupId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalIdentityId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnterpriseGroupMembers", x => new { x.GroupId, x.ExternalIdentityId });
                    table.ForeignKey(
                        name: "FK_EnterpriseGroupMembers_EnterpriseExternalIdentities_ExternalIdentityId",
                        column: x => x.ExternalIdentityId,
                        principalTable: "EnterpriseExternalIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EnterpriseGroupMembers_EnterpriseGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "EnterpriseGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseGroupMembers_ExternalIdentityId",
                table: "EnterpriseGroupMembers",
                column: "ExternalIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseGroups_ConnectionId_DisplayName",
                table: "EnterpriseGroups",
                columns: new[] { "ConnectionId", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseGroups_ConnectionId_ExternalId",
                table: "EnterpriseGroups",
                columns: new[] { "ConnectionId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseScimCredentials_Prefix",
                table: "EnterpriseScimCredentials",
                column: "Prefix",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnterpriseGroupMembers");

            migrationBuilder.DropTable(
                name: "EnterpriseScimCredentials");

            migrationBuilder.DropTable(
                name: "EnterpriseGroups");
        }
    }
}
