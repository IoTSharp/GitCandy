using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class RequiredReviewsAndCodeOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DismissStaleApprovals",
                table: "BranchProtectionRules",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireCodeOwnerReviews",
                table: "BranchProtectionRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RequiredApprovals",
                table: "BranchProtectionRules",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DismissStaleApprovals",
                table: "BranchProtectionRules");

            migrationBuilder.DropColumn(
                name: "RequireCodeOwnerReviews",
                table: "BranchProtectionRules");

            migrationBuilder.DropColumn(
                name: "RequiredApprovals",
                table: "BranchProtectionRules");
        }
    }
}
