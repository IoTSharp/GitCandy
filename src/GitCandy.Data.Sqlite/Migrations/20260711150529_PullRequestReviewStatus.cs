using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class PullRequestReviewStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssigneeUserId",
                table: "PullRequests",
                type: "TEXT",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PullRequestReviewers",
                columns: table => new
                {
                    PullRequestId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReviewerUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestReviewers", x => new { x.PullRequestId, x.ReviewerUserId });
                    table.ForeignKey(
                        name: "FK_PullRequestReviewers_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequestReviewers_AspNetUsers_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequestReviewers_PullRequests_PullRequestId",
                        column: x => x.PullRequestId,
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PullRequestReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PullRequestId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReviewerUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "TEXT", maxLength: 131072, nullable: false),
                    HeadSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReviewerRequestVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DismissedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    DismissedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DismissalReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequestReviews_AspNetUsers_DismissedByUserId",
                        column: x => x.DismissedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PullRequestReviews_AspNetUsers_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequestReviews_PullRequests_PullRequestId",
                        column: x => x.PullRequestId,
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_AssigneeUserId",
                table: "PullRequests",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewers_PullRequestId_RequestedAtUtc",
                table: "PullRequestReviewers",
                columns: new[] { "PullRequestId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewers_RequestedByUserId",
                table: "PullRequestReviewers",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewers_ReviewerUserId",
                table: "PullRequestReviewers",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviews_DismissedByUserId",
                table: "PullRequestReviews",
                column: "DismissedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviews_PullRequestId_ReviewerUserId_SubmittedAtUtc_Id",
                table: "PullRequestReviews",
                columns: new[] { "PullRequestId", "ReviewerUserId", "SubmittedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviews_ReviewerUserId",
                table: "PullRequestReviews",
                column: "ReviewerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PullRequests_AspNetUsers_AssigneeUserId",
                table: "PullRequests",
                column: "AssigneeUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PullRequests_AspNetUsers_AssigneeUserId",
                table: "PullRequests");

            migrationBuilder.DropTable(
                name: "PullRequestReviewers");

            migrationBuilder.DropTable(
                name: "PullRequestReviews");

            migrationBuilder.DropIndex(
                name: "IX_PullRequests_AssigneeUserId",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "AssigneeUserId",
                table: "PullRequests");
        }
    }
}
