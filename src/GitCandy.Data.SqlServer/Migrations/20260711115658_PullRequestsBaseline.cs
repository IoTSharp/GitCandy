using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class PullRequestsBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PullRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: false),
                    AuthorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SourceBranch = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    TargetBranch = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    OriginalBaseSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OriginalHeadSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CurrentBaseSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CurrentHeadSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsDraft = table.Column<bool>(type: "bit", nullable: false),
                    ActivePairKey = table.Column<string>(type: "nvarchar(520)", maxLength: 520, nullable: false),
                    MergeCommitSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    MergedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MergedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequests_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequests_AspNetUsers_MergedByUserId",
                        column: x => x.MergedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequests_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PullRequestTimelineEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PullRequestId = table.Column<long>(type: "bigint", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestTimelineEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequestTimelineEvents_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PullRequestTimelineEvents_PullRequests_PullRequestId",
                        column: x => x.PullRequestId,
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_AuthorUserId",
                table: "PullRequests",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_MergedByUserId",
                table: "PullRequests",
                column: "MergedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_RepositoryId_ActivePairKey",
                table: "PullRequests",
                columns: new[] { "RepositoryId", "ActivePairKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_RepositoryId_Number",
                table: "PullRequests",
                columns: new[] { "RepositoryId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_RepositoryId_State_UpdatedAtUtc",
                table: "PullRequests",
                columns: new[] { "RepositoryId", "State", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestTimelineEvents_ActorUserId",
                table: "PullRequestTimelineEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestTimelineEvents_PullRequestId_CreatedAtUtc",
                table: "PullRequestTimelineEvents",
                columns: new[] { "PullRequestId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PullRequestTimelineEvents");

            migrationBuilder.DropTable(
                name: "PullRequests");
        }
    }
}
