using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.Sqlite.Migrations
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
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    AuthorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    SourceBranch = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    TargetBranch = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    OriginalBaseSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OriginalHeadSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CurrentBaseSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CurrentHeadSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsDraft = table.Column<bool>(type: "INTEGER", nullable: false),
                    ActivePairKey = table.Column<string>(type: "TEXT", maxLength: 520, nullable: false),
                    MergeCommitSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MergedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MergedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
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
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PullRequestId = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
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
