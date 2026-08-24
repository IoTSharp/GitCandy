using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class M15RemoteMirrorJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RepositoryMirrors_Target_Direction",
                table: "RepositoryMirrors");

            migrationBuilder.CreateTable(
                name: "RemoteMirrorJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MirrorId = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Triggers = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    ProcessedGeneration = table.Column<long>(type: "INTEGER", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancellationRequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    LastStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteMirrorJobs", x => x.Id);
                    table.CheckConstraint("CK_RemoteMirrorJobs_AttemptCount", "AttemptCount >= 0");
                    table.CheckConstraint("CK_RemoteMirrorJobs_Generation", "RequestedGeneration >= 1 AND ProcessedGeneration >= 0 AND ProcessedGeneration <= RequestedGeneration");
                    table.CheckConstraint("CK_RemoteMirrorJobs_Lease", "(State = 'Leased' AND LeaseOwner IS NOT NULL AND LeaseExpiresAtUtc IS NOT NULL) OR (State <> 'Leased' AND LeaseOwner IS NULL AND LeaseExpiresAtUtc IS NULL)");
                    table.ForeignKey(
                        name: "FK_RemoteMirrorJobs_RepositoryMirrors_MirrorId",
                        column: x => x.MirrorId,
                        principalTable: "RepositoryMirrors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryMirrors_Target_Direction",
                table: "RepositoryMirrors",
                columns: new[] { "RepositoryId", "ConnectionId", "RemoteRepositoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemoteMirrorJobs_LeaseExpiresAtUtc",
                table: "RemoteMirrorJobs",
                column: "LeaseExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RemoteMirrorJobs_MirrorId",
                table: "RemoteMirrorJobs",
                column: "MirrorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemoteMirrorJobs_State_AvailableAtUtc",
                table: "RemoteMirrorJobs",
                columns: new[] { "State", "AvailableAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemoteMirrorJobs");

            migrationBuilder.DropIndex(
                name: "IX_RepositoryMirrors_Target_Direction",
                table: "RepositoryMirrors");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryMirrors_Target_Direction",
                table: "RepositoryMirrors",
                columns: new[] { "RepositoryId", "ConnectionId", "RemoteRepositoryId", "Direction" },
                unique: true);
        }
    }
}
