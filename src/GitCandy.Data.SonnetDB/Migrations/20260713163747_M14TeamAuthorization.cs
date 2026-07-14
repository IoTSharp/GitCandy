using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SonnetDB.Migrations
{
    /// <inheritdoc />
    public partial class M14TeamAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    TeamId = table.Column<long>(type: "INT", nullable: true),
                    TeamName = table.Column<string>(type: "STRING", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    ActorName = table.Column<string>(type: "STRING", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "STRING", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "STRING", maxLength: 128, nullable: false),
                    Detail = table.Column<string>(type: "STRING", maxLength: 1000, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamAuditEvents_TeamId_OccurredAtUtc",
                table: "TeamAuditEvents",
                columns: new[] { "TeamId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamAuditEvents");
        }
    }
}
