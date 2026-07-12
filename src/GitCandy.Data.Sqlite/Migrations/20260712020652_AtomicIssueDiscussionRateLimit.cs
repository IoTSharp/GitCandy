using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AtomicIssueDiscussionRateLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IssueTimelineEvents_ActorUserId",
                table: "IssueTimelineEvents");

            migrationBuilder.CreateIndex(
                name: "IX_IssueTimelineEvents_ActorUserId_Type_CreatedAtUtc",
                table: "IssueTimelineEvents",
                columns: new[] { "ActorUserId", "Type", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IssueTimelineEvents_ActorUserId_Type_CreatedAtUtc",
                table: "IssueTimelineEvents");

            migrationBuilder.CreateIndex(
                name: "IX_IssueTimelineEvents_ActorUserId",
                table: "IssueTimelineEvents",
                column: "ActorUserId");
        }
    }
}
