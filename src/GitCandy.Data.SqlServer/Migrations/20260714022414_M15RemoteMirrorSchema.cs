using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class M15RemoteMirrorSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RemoteAccountConnections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OwnerTeamId = table.Column<long>(type: "bigint", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ServerUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ExternalAccountId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AccountKind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Login = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AuthenticationKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CredentialReference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    GrantedScopes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    LastErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastTestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemoteAccountConnections", x => x.Id);
                    table.CheckConstraint("CK_RemoteAccountConnections_Owner", "(OwnerKind = 'User' AND OwnerUserId IS NOT NULL AND OwnerTeamId IS NULL) OR (OwnerKind = 'Team' AND OwnerUserId IS NULL AND OwnerTeamId IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_RemoteAccountConnections_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RemoteAccountConnections_Teams_OwnerTeamId",
                        column: x => x.OwnerTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryMirrors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    ConnectionId = table.Column<long>(type: "bigint", nullable: false),
                    RemoteRepositoryId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RemoteOwnerLogin = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RemoteRepositoryName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RemoteGitUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Authority = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RefFilterKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RefFilterPattern = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ScheduleIntervalMinutes = table.Column<int>(type: "int", nullable: true),
                    ScheduleTimeZone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScheduleEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DivergencePolicy = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Prune = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    LastObservedRemoteHead = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastAttemptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSucceededAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryMirrors", x => x.Id);
                    table.CheckConstraint("CK_RepositoryMirrors_DirectionAuthority", "(Direction = 'Pull' AND Authority = 'Remote') OR (Direction = 'Push' AND Authority = 'GitCandy')");
                    table.CheckConstraint("CK_RepositoryMirrors_RefFilter", "(RefFilterKind IN ('AllRefs', 'ProtectedBranches') AND RefFilterPattern IS NULL) OR (RefFilterKind IN ('AllowList', 'RegularExpression') AND RefFilterPattern IS NOT NULL)");
                    table.CheckConstraint("CK_RepositoryMirrors_ScheduleConfiguration", "(ScheduleIntervalMinutes IS NULL AND ScheduleTimeZone IS NULL) OR (ScheduleIntervalMinutes IS NOT NULL AND ScheduleTimeZone IS NOT NULL)");
                    table.CheckConstraint("CK_RepositoryMirrors_ScheduleInterval", "ScheduleIntervalMinutes IS NULL OR (ScheduleIntervalMinutes >= 5 AND ScheduleIntervalMinutes <= 10080)");
                    table.ForeignKey(
                        name: "FK_RepositoryMirrors_RemoteAccountConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "RemoteAccountConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepositoryMirrors_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteAccountConnections_StableIdentity",
                table: "RemoteAccountConnections",
                columns: new[] { "Provider", "ServerUrl", "ExternalAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RemoteAccountConnections_Team_Enabled",
                table: "RemoteAccountConnections",
                columns: new[] { "OwnerTeamId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_RemoteAccountConnections_User_Enabled",
                table: "RemoteAccountConnections",
                columns: new[] { "OwnerUserId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryMirrors_ConnectionId",
                table: "RepositoryMirrors",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryMirrors_Schedule_Status",
                table: "RepositoryMirrors",
                columns: new[] { "ScheduleEnabled", "IsEnabled", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryMirrors_Target_Direction",
                table: "RepositoryMirrors",
                columns: new[] { "RepositoryId", "ConnectionId", "RemoteRepositoryId", "Direction" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepositoryMirrors");

            migrationBuilder.DropTable(
                name: "RemoteAccountConnections");
        }
    }
}
