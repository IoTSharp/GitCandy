using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class M14TeamGovernanceRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserTeamRoles_TeamId",
                table: "UserTeamRoles");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "UserTeamRoles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Member");

            migrationBuilder.Sql(
                "UPDATE UserTeamRoles SET Role = 'TeamOwner' WHERE IsAdministrator = 1");

            migrationBuilder.DropColumn(
                name: "IsAdministrator",
                table: "UserTeamRoles");

            migrationBuilder.CreateIndex(
                name: "IX_UserTeamRoles_TeamId_Role",
                table: "UserTeamRoles",
                columns: new[] { "TeamId", "Role" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserTeamRoles_Role",
                table: "UserTeamRoles",
                sql: "Role IN ('Member', 'DeputyLeader', 'Leader', 'TeamOwner')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserTeamRoles_TeamId_Role",
                table: "UserTeamRoles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserTeamRoles_Role",
                table: "UserTeamRoles");

            migrationBuilder.AddColumn<bool>(
                name: "IsAdministrator",
                table: "UserTeamRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE UserTeamRoles SET IsAdministrator = 1 WHERE Role = 'TeamOwner'");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "UserTeamRoles");

            migrationBuilder.CreateIndex(
                name: "IX_UserTeamRoles_TeamId",
                table: "UserTeamRoles",
                column: "TeamId");
        }
    }
}
