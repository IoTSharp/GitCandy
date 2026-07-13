using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GitCandy.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceDashboardDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityEvents",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: true),
                    TeamId = table.Column<long>(type: "INTEGER", nullable: true),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RetainUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEvents", x => x.EventId);
                    table.ForeignKey(
                        name: "FK_ActivityEvents_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityEvents_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: true),
                    TeamId = table.Column<long>(type: "INTEGER", nullable: true),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryInteractions",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    LastInteractedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InteractionCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryInteractions", x => new { x.UserId, x.RepositoryId });
                    table.ForeignKey(
                        name: "FK_RepositoryInteractions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepositoryInteractions_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryMetricsDaily",
                columns: table => new
                {
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    DayUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CommitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveCommitDays = table.Column<int>(type: "INTEGER", nullable: false),
                    StarCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StarNetGrowth = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessfulDownloadCount = table.Column<long>(type: "INTEGER", nullable: false),
                    SuccessfulGitFetchCount = table.Column<long>(type: "INTEGER", nullable: false),
                    UniquePageViewCount = table.Column<long>(type: "INTEGER", nullable: false),
                    LicenseSpdx = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryMetricsDaily", x => new { x.RepositoryId, x.DayUtc });
                    table.ForeignKey(
                        name: "FK_RepositoryMetricsDaily_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryPageViews",
                columns: table => new
                {
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    DayUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VisitorKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryPageViews", x => new { x.RepositoryId, x.DayUtc, x.VisitorKey });
                    table.ForeignKey(
                        name: "FK_RepositoryPageViews_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryRecommendationSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CommitScore = table.Column<double>(type: "REAL", nullable: false),
                    StarScore = table.Column<double>(type: "REAL", nullable: false),
                    DownloadScore = table.Column<double>(type: "REAL", nullable: false),
                    PageViewScore = table.Column<double>(type: "REAL", nullable: false),
                    TotalScore = table.Column<double>(type: "REAL", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryRecommendationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoryRecommendationSnapshots_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryStars",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryStars", x => new { x.UserId, x.RepositoryId });
                    table.ForeignKey(
                        name: "FK_RepositoryStars_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepositoryStars_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Todos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: true),
                    TeamId = table.Column<long>(type: "INTEGER", nullable: true),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SnoozedUntilUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Todos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Todos_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Todos_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Todos_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "NamespaceClaims",
                columns: new[] { "NormalizedSlug", "ClaimType", "NamespaceAliasId", "NamespaceId", "Slug" },
                values: new object?[,]
                {
                    { "EXPLORE", 3, null, null, "explore" },
                    { "ME", 3, null, null, "me" },
                    { "NOTIFICATIONS", 3, null, null, "notifications" },
                    { "TODOS", 3, null, null, "todos" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_Repository_Occurred_Event",
                table: "ActivityEvents",
                columns: new[] { "RepositoryId", "OccurredAtUtc", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_RetainUntilUtc",
                table: "ActivityEvents",
                column: "RetainUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_TeamId",
                table: "ActivityEvents",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RepositoryId",
                table: "Notifications",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TeamId",
                table: "Notifications",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_User_Event",
                table: "Notifications",
                columns: new[] { "UserId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_User_Read_Created",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryInteractions_RepositoryId",
                table: "RepositoryInteractions",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryInteractions_User_Last",
                table: "RepositoryInteractions",
                columns: new[] { "UserId", "LastInteractedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryMetricsDaily_DayUtc",
                table: "RepositoryMetricsDaily",
                column: "DayUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryPageViews_DayUtc",
                table: "RepositoryPageViews",
                column: "DayUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationSnapshots_Calculated_Rank",
                table: "RepositoryRecommendationSnapshots",
                columns: new[] { "CalculatedAtUtc", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationSnapshots_Snapshot_Repository",
                table: "RepositoryRecommendationSnapshots",
                columns: new[] { "SnapshotId", "RepositoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryRecommendationSnapshots_RepositoryId",
                table: "RepositoryRecommendationSnapshots",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryStars_Repository_Created",
                table: "RepositoryStars",
                columns: new[] { "RepositoryId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Todos_RepositoryId",
                table: "Todos",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Todos_TeamId",
                table: "Todos",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Todos_User_Kind_Resource",
                table: "Todos",
                columns: new[] { "UserId", "Kind", "ResourceType", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Todos_User_Status_Snooze_Updated",
                table: "Todos",
                columns: new[] { "UserId", "Status", "SnoozedUntilUtc", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEvents");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "RepositoryInteractions");

            migrationBuilder.DropTable(
                name: "RepositoryMetricsDaily");

            migrationBuilder.DropTable(
                name: "RepositoryPageViews");

            migrationBuilder.DropTable(
                name: "RepositoryRecommendationSnapshots");

            migrationBuilder.DropTable(
                name: "RepositoryStars");

            migrationBuilder.DropTable(
                name: "Todos");

            migrationBuilder.DeleteData(
                table: "NamespaceClaims",
                keyColumn: "NormalizedSlug",
                keyValue: "EXPLORE");

            migrationBuilder.DeleteData(
                table: "NamespaceClaims",
                keyColumn: "NormalizedSlug",
                keyValue: "ME");

            migrationBuilder.DeleteData(
                table: "NamespaceClaims",
                keyColumn: "NormalizedSlug",
                keyValue: "NOTIFICATIONS");

            migrationBuilder.DeleteData(
                table: "NamespaceClaims",
                keyColumn: "NormalizedSlug",
                keyValue: "TODOS");
        }
    }
}
