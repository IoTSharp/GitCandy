using Microsoft.EntityFrameworkCore.Migrations;


namespace GitCandy.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryForkNetwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ForkNetworkRoot",
                table: "Repositories",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForkedFromRepository",
                table: "Repositories",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ForkNetworkRoot",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "ForkedFromRepository",
                table: "Repositories");
        }
    }
}
