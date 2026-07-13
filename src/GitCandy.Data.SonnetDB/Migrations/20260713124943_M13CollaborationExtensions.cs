using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SonnetDB.Migrations
{
    /// <inheritdoc />
    public partial class M13CollaborationExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventType",
                table: "Notifications",
                type: "STRING",
                maxLength: 24,
                nullable: false,
                defaultValue: "Issue");

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "STRING", maxLength: 32, nullable: false),
                    NotificationId = table.Column<long>(type: "INT", nullable: false),
                    Channel = table.Column<string>(type: "STRING", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "STRING", maxLength: 16, nullable: false),
                    Recipient = table.Column<string>(type: "STRING", maxLength: 2048, nullable: false),
                    ProtectedSecret = table.Column<string>(type: "STRING", maxLength: 4096, nullable: true),
                    AttemptCount = table.Column<int>(type: "INT", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    ResponseStatusCode = table.Column<int>(type: "INT", nullable: true),
                    ErrorCode = table.Column<string>(type: "STRING", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    EventType = table.Column<string>(type: "STRING", maxLength: 24, nullable: false),
                    EmailEnabled = table.Column<bool>(type: "BOOL", nullable: false),
                    WebhookEnabled = table.Column<bool>(type: "BOOL", nullable: false),
                    WebhookUrl = table.Column<string>(type: "STRING", maxLength: 2048, nullable: true),
                    ProtectedWebhookSecret = table.Column<string>(type: "STRING", maxLength: 4096, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => new { x.UserId, x.EventType });
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Releases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    TagName = table.Column<string>(type: "STRING", maxLength: 255, nullable: false),
                    NormalizedTagName = table.Column<string>(type: "STRING", maxLength: 255, nullable: false),
                    TagCommitSha = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "STRING", maxLength: 200, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "STRING", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "STRING", maxLength: 65536, nullable: false),
                    IsDraft = table.Column<bool>(type: "BOOL", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Releases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Releases_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseAssets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "STRING", maxLength: 32, nullable: false),
                    ReleaseId = table.Column<long>(type: "INT", nullable: false),
                    FileName = table.Column<string>(type: "STRING", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "STRING", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "INT", nullable: false),
                    Sha256 = table.Column<string>(type: "STRING", fixedLength: true, maxLength: 64, nullable: false),
                    DownloadCount = table.Column<long>(type: "INT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseAssets_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Notification_Channel",
                table: "NotificationDeliveries",
                columns: new[] { "NotificationId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_State_NextAttempt",
                table: "NotificationDeliveries",
                columns: new[] { "State", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAssets_Release_FileName",
                table: "ReleaseAssets",
                columns: new[] { "ReleaseId", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Releases_Repository_Published",
                table: "Releases",
                columns: new[] { "RepositoryId", "PublishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Releases_Repository_Tag",
                table: "Releases",
                columns: new[] { "RepositoryId", "NormalizedTagName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "ReleaseAssets");

            migrationBuilder.DropTable(
                name: "Releases");

            migrationBuilder.DropColumn(
                name: "EventType",
                table: "Notifications");
        }
    }
}
