using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GitCandy.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class IssuesAndDiscussions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IssueLabels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 6, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueLabels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueLabels_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueMilestones",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsClosed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueMilestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueMilestones_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemSequences",
                columns: table => new
                {
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    NextNumber = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemSequences", x => x.RepositoryId);
                    table.ForeignKey(
                        name: "FK_WorkItemSequences_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                "INSERT INTO WorkItemSequences (RepositoryId, NextNumber, Version) SELECT Id, 1, 0 FROM Repositories;");

            migrationBuilder.CreateTable(
                name: "Issues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "TEXT", maxLength: 131072, nullable: false),
                    AuthorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    AssigneeUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    MilestoneId = table.Column<long>(type: "INTEGER", nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Issues_AspNetUsers_AssigneeUserId",
                        column: x => x.AssigneeUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Issues_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Issues_IssueMilestones_MilestoneId",
                        column: x => x.MilestoneId,
                        principalTable: "IssueMilestones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Issues_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueComments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<long>(type: "INTEGER", nullable: false),
                    AuthorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "TEXT", maxLength: 131072, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EditedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsHidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    HiddenByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    HiddenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueComments_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IssueComments_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueLabelLinks",
                columns: table => new
                {
                    IssueId = table.Column<long>(type: "INTEGER", nullable: false),
                    LabelId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueLabelLinks", x => new { x.IssueId, x.LabelId });
                    table.ForeignKey(
                        name: "FK_IssueLabelLinks_IssueLabels_LabelId",
                        column: x => x.LabelId,
                        principalTable: "IssueLabels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IssueLabelLinks_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    IssueId = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueNotifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IssueNotifications_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IssueNotifications_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IssueReferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceIssueId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetRepositoryId = table.Column<long>(type: "INTEGER", nullable: true),
                    TargetIssueId = table.Column<long>(type: "INTEGER", nullable: true),
                    CommitSha = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DisplayText = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueReferences_Issues_SourceIssueId",
                        column: x => x.SourceIssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IssueReferences_Issues_TargetIssueId",
                        column: x => x.TargetIssueId,
                        principalTable: "Issues",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IssueReferences_Repositories_TargetRepositoryId",
                        column: x => x.TargetRepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IssueRelations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RepositoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceIssueId = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetIssueId = table.Column<long>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueRelations_Issues_SourceIssueId",
                        column: x => x.SourceIssueId,
                        principalTable: "Issues",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IssueRelations_Issues_TargetIssueId",
                        column: x => x.TargetIssueId,
                        principalTable: "Issues",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IssueRelations_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueSubscriptions",
                columns: table => new
                {
                    IssueId = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    IsSubscribed = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueSubscriptions", x => new { x.IssueId, x.UserId });
                    table.ForeignKey(
                        name: "FK_IssueSubscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IssueSubscriptions_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueEdits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<long>(type: "INTEGER", nullable: false),
                    CommentId = table.Column<long>(type: "INTEGER", nullable: true),
                    EditorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    PreviousMarkdown = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: false),
                    PreviousHtml = table.Column<string>(type: "TEXT", maxLength: 131072, nullable: false),
                    EditedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueEdits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueEdits_IssueComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "IssueComments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IssueEdits_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueTimelineEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<long>(type: "INTEGER", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CommentId = table.Column<long>(type: "INTEGER", nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueTimelineEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueTimelineEvents_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IssueTimelineEvents_IssueComments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "IssueComments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_IssueTimelineEvents_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueComments_AuthorUserId",
                table: "IssueComments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueComments_IssueId_CreatedAtUtc",
                table: "IssueComments",
                columns: new[] { "IssueId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueEdits_CommentId",
                table: "IssueEdits",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueEdits_IssueId_EditedAtUtc",
                table: "IssueEdits",
                columns: new[] { "IssueId", "EditedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueLabelLinks_LabelId",
                table: "IssueLabelLinks",
                column: "LabelId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueLabels_RepositoryId_NormalizedName",
                table: "IssueLabels",
                columns: new[] { "RepositoryId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueMilestones_RepositoryId_Title",
                table: "IssueMilestones",
                columns: new[] { "RepositoryId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueNotifications_IssueId",
                table: "IssueNotifications",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueNotifications_RepositoryId",
                table: "IssueNotifications",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueNotifications_UserId_ReadAtUtc_CreatedAtUtc",
                table: "IssueNotifications",
                columns: new[] { "UserId", "ReadAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueReferences_SourceIssueId",
                table: "IssueReferences",
                column: "SourceIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueReferences_TargetIssueId",
                table: "IssueReferences",
                column: "TargetIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueReferences_TargetRepositoryId",
                table: "IssueReferences",
                column: "TargetRepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueRelations_RepositoryId",
                table: "IssueRelations",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueRelations_Source_Target_Type",
                table: "IssueRelations",
                columns: new[] { "SourceIssueId", "TargetIssueId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueRelations_TargetIssueId",
                table: "IssueRelations",
                column: "TargetIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_AssigneeUserId",
                table: "Issues",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_AuthorUserId",
                table: "Issues",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_MilestoneId",
                table: "Issues",
                column: "MilestoneId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_RepositoryId_Number",
                table: "Issues",
                columns: new[] { "RepositoryId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Issues_RepositoryId_State_UpdatedAtUtc",
                table: "Issues",
                columns: new[] { "RepositoryId", "State", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueSubscriptions_UserId",
                table: "IssueSubscriptions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueTimelineEvents_ActorUserId",
                table: "IssueTimelineEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueTimelineEvents_CommentId",
                table: "IssueTimelineEvents",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueTimelineEvents_IssueId_CreatedAtUtc_Id",
                table: "IssueTimelineEvents",
                columns: new[] { "IssueId", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueEdits");

            migrationBuilder.DropTable(
                name: "IssueLabelLinks");

            migrationBuilder.DropTable(
                name: "IssueNotifications");

            migrationBuilder.DropTable(
                name: "IssueReferences");

            migrationBuilder.DropTable(
                name: "IssueRelations");

            migrationBuilder.DropTable(
                name: "IssueSubscriptions");

            migrationBuilder.DropTable(
                name: "IssueTimelineEvents");

            migrationBuilder.DropTable(
                name: "WorkItemSequences");

            migrationBuilder.DropTable(
                name: "IssueLabels");

            migrationBuilder.DropTable(
                name: "IssueComments");

            migrationBuilder.DropTable(
                name: "Issues");

            migrationBuilder.DropTable(
                name: "IssueMilestones");
        }
    }
}
