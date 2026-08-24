using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SonnetDB.Migrations
{
    /// <inheritdoc />
    public partial class M15RemoteProviderLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CredentialExpiresAtUtc",
                table: "RemoteAccountConnections",
                type: "DATETIME",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookSecretReference",
                table: "RemoteAccountConnections",
                type: "STRING",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RemoteProviderEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false)
                        .Annotation("SonnetDB:AutoIncrement", true),
                    ConnectionId = table.Column<long>(type: "INT", nullable: false),
                    Provider = table.Column<string>(type: "STRING", maxLength: 16, nullable: false),
                    DeliveryId = table.Column<string>(type: "STRING", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "STRING", maxLength: 128, nullable: false),
                    PayloadHash = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteProviderEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemoteProviderEvents_RemoteAccountConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "RemoteAccountConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteProviderEvents_Connection_Delivery",
                table: "RemoteProviderEvents",
                columns: new[] { "ConnectionId", "DeliveryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemoteProviderEvents_ReceivedAtUtc",
                table: "RemoteProviderEvents",
                column: "ReceivedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemoteProviderEvents");

            migrationBuilder.DropColumn(
                name: "CredentialExpiresAtUtc",
                table: "RemoteAccountConnections");

            migrationBuilder.DropColumn(
                name: "WebhookSecretReference",
                table: "RemoteAccountConnections");
        }
    }
}
