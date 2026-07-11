using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class PullRequestReviewThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PullRequestReviewThreads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PullRequestId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OriginalBaseSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OriginalHeadSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OriginalPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    OriginalSide = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    OriginalStartLine = table.Column<int>(type: "int", nullable: false),
                    OriginalEndLine = table.Column<int>(type: "int", nullable: false),
                    AnchorContext = table.Column<string>(type: "nvarchar(max)", maxLength: 8192, nullable: false),
                    CurrentHeadSha = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CurrentPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CurrentSide = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    CurrentStartLine = table.Column<int>(type: "int", nullable: true),
                    CurrentEndLine = table.Column<int>(type: "int", nullable: true),
                    IsOutdated = table.Column<bool>(type: "bit", nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestReviewThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequestReviewThreads_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequestReviewThreads_AspNetUsers_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PullRequestReviewThreads_PullRequests_PullRequestId",
                        column: x => x.PullRequestId,
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PullRequestReviewComments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ThreadId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "nvarchar(max)", maxLength: 131072, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestReviewComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequestReviewComments_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequestReviewComments_PullRequestReviewThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "PullRequestReviewThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewComments_AuthorUserId",
                table: "PullRequestReviewComments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewComments_ThreadId_CreatedAtUtc_Id",
                table: "PullRequestReviewComments",
                columns: new[] { "ThreadId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewThreads_AuthorUserId",
                table: "PullRequestReviewThreads",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewThreads_PullRequestId_CreatedAtUtc_Id",
                table: "PullRequestReviewThreads",
                columns: new[] { "PullRequestId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewThreads_PullRequestId_Status",
                table: "PullRequestReviewThreads",
                columns: new[] { "PullRequestId", "IsOutdated", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewThreads_ResolvedByUserId",
                table: "PullRequestReviewThreads",
                column: "ResolvedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PullRequestReviewComments");

            migrationBuilder.DropTable(
                name: "PullRequestReviewThreads");
        }
    }
}
