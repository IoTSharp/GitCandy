using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class M15PullPushMirrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RemoteMirrorRefUpdates",
                columns: table => new
                {
                    MirrorId = table.Column<long>(type: "bigint", nullable: false),
                    ReferenceName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    OldObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NewObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    EnqueuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteMirrorRefUpdates", x => new { x.MirrorId, x.ReferenceName });
                    table.ForeignKey(
                        name: "FK_RemoteMirrorRefUpdates_RepositoryMirrors_MirrorId",
                        column: x => x.MirrorId,
                        principalTable: "RepositoryMirrors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteMirrorRefUpdates_UpdatedAtUtc",
                table: "RemoteMirrorRefUpdates",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemoteMirrorRefUpdates");
        }
    }
}
