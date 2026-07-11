using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace GitCandy.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class PullRequestMergeWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ForkNetworkRootRepositoryId",
                table: "Repositories",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ForkedFromRepositoryId",
                table: "Repositories",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceNamespaceSnapshot",
                table: "PullRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "SourceRepositoryId",
                table: "PullRequests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRepositorySnapshot",
                table: "PullRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE PullRequests
                SET SourceRepositoryId = RepositoryId,
                    SourceNamespaceSnapshot = COALESCE((
                        SELECT Namespaces.Slug
                        FROM Repositories
                        INNER JOIN Namespaces ON Namespaces.Id = Repositories.NamespaceId
                        WHERE Repositories.Id = PullRequests.RepositoryId), 'legacy'),
                    SourceRepositorySnapshot = COALESCE((
                        SELECT Repositories.Name
                        FROM Repositories
                        WHERE Repositories.Id = PullRequests.RepositoryId), '');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_ForkedFromRepositoryId",
                table: "Repositories",
                column: "ForkedFromRepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_ForkNetworkRootRepositoryId",
                table: "Repositories",
                column: "ForkNetworkRootRepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_SourceRepositoryId",
                table: "PullRequests",
                column: "SourceRepositoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_PullRequests_Repositories_SourceRepositoryId",
                table: "PullRequests",
                column: "SourceRepositoryId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Repositories_Repositories_ForkNetworkRootRepositoryId",
                table: "Repositories",
                column: "ForkNetworkRootRepositoryId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Repositories_Repositories_ForkedFromRepositoryId",
                table: "Repositories",
                column: "ForkedFromRepositoryId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PullRequests_Repositories_SourceRepositoryId",
                table: "PullRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Repositories_Repositories_ForkNetworkRootRepositoryId",
                table: "Repositories");

            migrationBuilder.DropForeignKey(
                name: "FK_Repositories_Repositories_ForkedFromRepositoryId",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_ForkedFromRepositoryId",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_ForkNetworkRootRepositoryId",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_PullRequests_SourceRepositoryId",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "ForkNetworkRootRepositoryId",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "ForkedFromRepositoryId",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "SourceNamespaceSnapshot",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "SourceRepositoryId",
                table: "PullRequests");

            migrationBuilder.DropColumn(
                name: "SourceRepositorySnapshot",
                table: "PullRequests");
        }
    }
}
