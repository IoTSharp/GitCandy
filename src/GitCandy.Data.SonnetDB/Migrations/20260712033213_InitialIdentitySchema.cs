using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GitCandy.Data.SonnetDB.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentitySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "STRING", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "STRING", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "STRING", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    DisplayName = table.Column<string>(type: "STRING", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "STRING", maxLength: 512, nullable: true),
                    UserName = table.Column<string>(type: "STRING", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "STRING", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "STRING", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "STRING", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "BOOL", nullable: false),
                    PasswordHash = table.Column<string>(type: "STRING", nullable: true),
                    SecurityStamp = table.Column<string>(type: "STRING", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "STRING", nullable: true),
                    PhoneNumber = table.Column<string>(type: "STRING", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "BOOL", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "BOOL", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "DATETIME", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "BOOL", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RenameEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    EventType = table.Column<int>(type: "INT", nullable: false),
                    SubjectType = table.Column<int>(type: "INT", nullable: false),
                    SubjectId = table.Column<long>(type: "INT", nullable: false),
                    ActorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    OldSlug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    NewSlug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    Reason = table.Column<string>(type: "STRING", maxLength: 500, nullable: true),
                    IsOverride = table.Column<bool>(type: "BOOL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenameEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    DisplayName = table.Column<string>(type: "STRING", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "STRING", maxLength: 20, nullable: false),
                    NormalizedName = table.Column<string>(type: "STRING", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "STRING", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INT", nullable: false),
                    RoleId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    ClaimType = table.Column<string>(type: "STRING", nullable: true),
                    ClaimValue = table.Column<string>(type: "STRING", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INT", nullable: false),
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    ClaimType = table.Column<string>(type: "STRING", nullable: true),
                    ClaimValue = table.Column<string>(type: "STRING", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "STRING", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "STRING", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "STRING", nullable: true),
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    RoleId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    LoginProvider = table.Column<string>(type: "STRING", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "STRING", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "STRING", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SshKeys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    KeyType = table.Column<string>(type: "STRING", maxLength: 20, nullable: false),
                    Fingerprint = table.Column<string>(type: "STRING", fixedLength: true, maxLength: 47, nullable: false),
                    PublicKey = table.Column<string>(type: "STRING", maxLength: 600, nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SshKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SshKeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Namespaces",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    OwnerType = table.Column<int>(type: "INT", nullable: false),
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    TeamId = table.Column<long>(type: "INT", nullable: true),
                    Slug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    NormalizedSlug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    Version = table.Column<int>(type: "INT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Namespaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Namespaces_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Namespaces_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserTeamRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    TeamId = table.Column<long>(type: "INT", nullable: false),
                    IsAdministrator = table.Column<bool>(type: "BOOL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTeamRoles", x => new { x.UserId, x.TeamId });
                    table.ForeignKey(
                        name: "FK_UserTeamRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserTeamRoles_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NamespaceAliases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    NamespaceId = table.Column<long>(type: "INT", nullable: false),
                    Slug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    NormalizedSlug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NamespaceAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NamespaceAliases_Namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalTable: "Namespaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    NamespaceId = table.Column<long>(type: "INT", nullable: false),
                    StorageName = table.Column<string>(type: "STRING", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    NormalizedName = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "STRING", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    IsPrivate = table.Column<bool>(type: "BOOL", nullable: false),
                    AllowAnonymousRead = table.Column<bool>(type: "BOOL", nullable: false),
                    AllowAnonymousWrite = table.Column<bool>(type: "BOOL", nullable: false),
                    ForkedFromRepository = table.Column<string>(type: "STRING", maxLength: 50, nullable: true),
                    ForkNetworkRoot = table.Column<string>(type: "STRING", maxLength: 50, nullable: true),
                    ForkedFromRepositoryId = table.Column<long>(type: "INT", nullable: true),
                    ForkNetworkRootRepositoryId = table.Column<long>(type: "INT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Repositories_Namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalTable: "Namespaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Repositories_Repositories_ForkNetworkRootRepositoryId",
                        column: x => x.ForkNetworkRootRepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Repositories_Repositories_ForkedFromRepositoryId",
                        column: x => x.ForkedFromRepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "NamespaceClaims",
                columns: table => new
                {
                    NormalizedSlug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    Slug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    ClaimType = table.Column<int>(type: "INT", nullable: false),
                    NamespaceId = table.Column<long>(type: "INT", nullable: true),
                    NamespaceAliasId = table.Column<long>(type: "INT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NamespaceClaims", x => x.NormalizedSlug);
                    table.ForeignKey(
                        name: "FK_NamespaceClaims_NamespaceAliases_NamespaceAliasId",
                        column: x => x.NamespaceAliasId,
                        principalTable: "NamespaceAliases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NamespaceClaims_Namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalTable: "Namespaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IssueLabels",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    Name = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    NormalizedName = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "STRING", fixedLength: true, maxLength: 6, nullable: false),
                    Description = table.Column<string>(type: "STRING", maxLength: 200, nullable: false),
                    IsArchived = table.Column<bool>(type: "BOOL", nullable: false)
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
                    Id = table.Column<long>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    Title = table.Column<string>(type: "STRING", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "STRING", maxLength: 2000, nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    IsClosed = table.Column<bool>(type: "BOOL", nullable: false),
                    IsArchived = table.Column<bool>(type: "BOOL", nullable: false)
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
                name: "LegacyRepositoryRoutes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    Project = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    NormalizedProject = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyRepositoryRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegacyRepositoryRoutes_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PullRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    SourceRepositoryId = table.Column<long>(type: "INT", nullable: true),
                    SourceNamespaceSnapshot = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    SourceRepositorySnapshot = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    Number = table.Column<long>(type: "INT", nullable: false),
                    Title = table.Column<string>(type: "STRING", maxLength: 256, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "STRING", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "STRING", maxLength: 65536, nullable: false),
                    AuthorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    AssigneeUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    SourceBranch = table.Column<string>(type: "STRING", maxLength: 255, nullable: false),
                    TargetBranch = table.Column<string>(type: "STRING", maxLength: 255, nullable: false),
                    OriginalBaseSha = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    OriginalHeadSha = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    CurrentBaseSha = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    CurrentHeadSha = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "STRING", maxLength: 16, nullable: false),
                    IsDraft = table.Column<bool>(type: "BOOL", nullable: false),
                    ActivePairKey = table.Column<string>(type: "STRING", maxLength: 520, nullable: false),
                    MergeCommitSha = table.Column<string>(type: "STRING", maxLength: 64, nullable: true),
                    MergedByUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    MergedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    Version = table.Column<long>(type: "INT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequests_AspNetUsers_AssigneeUserId",
                        column: x => x.AssigneeUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PullRequests_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequests_AspNetUsers_MergedByUserId",
                        column: x => x.MergedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequests_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PullRequests_Repositories_SourceRepositoryId",
                        column: x => x.SourceRepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryAliases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    NamespaceId = table.Column<long>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    Slug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    NormalizedSlug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoryAliases_Namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalTable: "Namespaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepositoryAliases_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamRepositoryRoles",
                columns: table => new
                {
                    TeamId = table.Column<long>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    AllowRead = table.Column<bool>(type: "BOOL", nullable: false),
                    AllowWrite = table.Column<bool>(type: "BOOL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamRepositoryRoles", x => new { x.TeamId, x.RepositoryId });
                    table.ForeignKey(
                        name: "FK_TeamRepositoryRoles_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamRepositoryRoles_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRepositoryRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    AllowRead = table.Column<bool>(type: "BOOL", nullable: false),
                    AllowWrite = table.Column<bool>(type: "BOOL", nullable: false),
                    IsOwner = table.Column<bool>(type: "BOOL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRepositoryRoles", x => new { x.UserId, x.RepositoryId });
                    table.ForeignKey(
                        name: "FK_UserRepositoryRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRepositoryRoles_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemSequences",
                columns: table => new
                {
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    NextNumber = table.Column<long>(type: "INT", nullable: false),
                    Version = table.Column<long>(type: "INT", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "Issues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    Number = table.Column<long>(type: "INT", nullable: false),
                    Title = table.Column<string>(type: "STRING", maxLength: 256, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "STRING", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "STRING", maxLength: 131072, nullable: false),
                    AuthorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    AssigneeUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    MilestoneId = table.Column<long>(type: "INT", nullable: true),
                    State = table.Column<string>(type: "STRING", maxLength: 16, nullable: false),
                    IsLocked = table.Column<bool>(type: "BOOL", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    Version = table.Column<long>(type: "INT", nullable: false)
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
                name: "PullRequestReviewers",
                columns: table => new
                {
                    PullRequestId = table.Column<long>(type: "INT", nullable: false),
                    ReviewerUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    Version = table.Column<long>(type: "INT", nullable: false)
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
                    Id = table.Column<long>(type: "INT", nullable: false),
                    PullRequestId = table.Column<long>(type: "INT", nullable: false),
                    ReviewerUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    State = table.Column<string>(type: "STRING", maxLength: 24, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "STRING", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "STRING", maxLength: 131072, nullable: false),
                    HeadSha = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    ReviewerRequestVersion = table.Column<long>(type: "INT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    DismissedByUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    DismissedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    DismissalReason = table.Column<string>(type: "STRING", maxLength: 1000, nullable: true),
                    Version = table.Column<long>(type: "INT", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "PullRequestReviewThreads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    PullRequestId = table.Column<long>(type: "INT", nullable: false),
                    AuthorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    OriginalBaseSha = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    OriginalHeadSha = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    OriginalPath = table.Column<string>(type: "STRING", maxLength: 1024, nullable: false),
                    OriginalSide = table.Column<string>(type: "STRING", maxLength: 8, nullable: false),
                    OriginalStartLine = table.Column<int>(type: "INT", nullable: false),
                    OriginalEndLine = table.Column<int>(type: "INT", nullable: false),
                    AnchorContext = table.Column<string>(type: "STRING", maxLength: 8192, nullable: false),
                    CurrentHeadSha = table.Column<string>(type: "STRING", maxLength: 64, nullable: false),
                    CurrentPath = table.Column<string>(type: "STRING", maxLength: 1024, nullable: true),
                    CurrentSide = table.Column<string>(type: "STRING", maxLength: 8, nullable: true),
                    CurrentStartLine = table.Column<int>(type: "INT", nullable: true),
                    CurrentEndLine = table.Column<int>(type: "INT", nullable: true),
                    IsOutdated = table.Column<bool>(type: "BOOL", nullable: false),
                    IsResolved = table.Column<bool>(type: "BOOL", nullable: false),
                    ResolvedByUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    Version = table.Column<long>(type: "INT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestReviewThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequestReviewThreads_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequestReviewThreads_AspNetUsers_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PullRequestReviewThreads_PullRequests_PullRequestId",
                        column: x => x.PullRequestId,
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PullRequestTimelineEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    PullRequestId = table.Column<long>(type: "INT", nullable: false),
                    ActorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    Type = table.Column<string>(type: "STRING", maxLength: 24, nullable: false),
                    Detail = table.Column<string>(type: "STRING", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestTimelineEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequestTimelineEvents_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PullRequestTimelineEvents_PullRequests_PullRequestId",
                        column: x => x.PullRequestId,
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryClaims",
                columns: table => new
                {
                    NamespaceId = table.Column<long>(type: "INT", nullable: false),
                    NormalizedSlug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    Slug = table.Column<string>(type: "STRING", maxLength: 50, nullable: false),
                    ClaimType = table.Column<int>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: true),
                    RepositoryAliasId = table.Column<long>(type: "INT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryClaims", x => new { x.NamespaceId, x.NormalizedSlug });
                    table.ForeignKey(
                        name: "FK_RepositoryClaims_Namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalTable: "Namespaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepositoryClaims_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepositoryClaims_RepositoryAliases_RepositoryAliasId",
                        column: x => x.RepositoryAliasId,
                        principalTable: "RepositoryAliases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IssueComments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    IssueId = table.Column<long>(type: "INT", nullable: false),
                    AuthorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "STRING", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "STRING", maxLength: 131072, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    EditedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    IsHidden = table.Column<bool>(type: "BOOL", nullable: false),
                    HiddenByUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    HiddenAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true),
                    Version = table.Column<long>(type: "INT", nullable: false)
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
                    IssueId = table.Column<long>(type: "INT", nullable: false),
                    LabelId = table.Column<long>(type: "INT", nullable: false)
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
                    Id = table.Column<long>(type: "INT", nullable: false),
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    IssueId = table.Column<long>(type: "INT", nullable: false),
                    ActorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    Type = table.Column<string>(type: "STRING", maxLength: 24, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: true)
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
                    Id = table.Column<long>(type: "INT", nullable: false),
                    SourceIssueId = table.Column<long>(type: "INT", nullable: false),
                    TargetRepositoryId = table.Column<long>(type: "INT", nullable: true),
                    TargetIssueId = table.Column<long>(type: "INT", nullable: true),
                    CommitSha = table.Column<string>(type: "STRING", maxLength: 64, nullable: true),
                    DisplayText = table.Column<string>(type: "STRING", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
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
                    Id = table.Column<long>(type: "INT", nullable: false),
                    RepositoryId = table.Column<long>(type: "INT", nullable: false),
                    SourceIssueId = table.Column<long>(type: "INT", nullable: false),
                    TargetIssueId = table.Column<long>(type: "INT", nullable: false),
                    Type = table.Column<string>(type: "STRING", maxLength: 16, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
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
                    IssueId = table.Column<long>(type: "INT", nullable: false),
                    UserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    IsSubscribed = table.Column<bool>(type: "BOOL", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
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
                name: "PullRequestReviewComments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    ThreadId = table.Column<long>(type: "INT", nullable: false),
                    AuthorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "STRING", maxLength: 65536, nullable: false),
                    BodyHtml = table.Column<string>(type: "STRING", maxLength: 131072, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false),
                    Version = table.Column<long>(type: "INT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestReviewComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullRequestReviewComments_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequestReviewComments_PullRequestReviewThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "PullRequestReviewThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueEdits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INT", nullable: false),
                    IssueId = table.Column<long>(type: "INT", nullable: false),
                    CommentId = table.Column<long>(type: "INT", nullable: true),
                    EditorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: false),
                    PreviousMarkdown = table.Column<string>(type: "STRING", maxLength: 65536, nullable: false),
                    PreviousHtml = table.Column<string>(type: "STRING", maxLength: 131072, nullable: false),
                    EditedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
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
                    Id = table.Column<long>(type: "INT", nullable: false),
                    IssueId = table.Column<long>(type: "INT", nullable: false),
                    ActorUserId = table.Column<string>(type: "STRING", maxLength: 450, nullable: true),
                    Type = table.Column<string>(type: "STRING", maxLength: 24, nullable: false),
                    CommentId = table.Column<long>(type: "INT", nullable: true),
                    Detail = table.Column<string>(type: "STRING", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME", nullable: false)
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

            migrationBuilder.InsertData(
                table: "NamespaceClaims",
                columns: new[] { "NormalizedSlug", "ClaimType", "NamespaceAliasId", "NamespaceId", "Slug" },
                values: new object?[,]
                {
                    { "ACCOUNT", 3, null, null, "account" },
                    { "API", 3, null, null, "api" },
                    { "ASSETS", 3, null, null, "assets" },
                    { "GIT", 3, null, null, "git" },
                    { "HEALTH", 3, null, null, "health" },
                    { "HOME", 3, null, null, "home" },
                    { "IDENTITY", 3, null, null, "identity" },
                    { "REPOSITORY", 3, null, null, "repository" },
                    { "SETTING", 3, null, null, "setting" },
                    { "SIGNIN-OIDC", 3, null, null, "signin-oidc" },
                    { "SIGNOUT-CALLBACK-OIDC", 3, null, null, "signout-callback-oidc" },
                    { "TEAM", 3, null, null, "team" }
                });

            migrationBuilder.InsertData(
                table: "Namespaces",
                columns: new[] { "Id", "CreatedAtUtc", "NormalizedSlug", "OwnerType", "Slug", "TeamId", "UserId", "Version" },
                values: new object?[] { 1L, new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "LEGACY", 0, "legacy", null, null, 0 });

            migrationBuilder.InsertData(
                table: "NamespaceClaims",
                columns: new[] { "NormalizedSlug", "ClaimType", "NamespaceAliasId", "NamespaceId", "Slug" },
                values: new object?[] { "LEGACY", 1, null, 1L, "legacy" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

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
                name: "IX_IssueTimelineEvents_ActorUserId_Type_CreatedAtUtc",
                table: "IssueTimelineEvents",
                columns: new[] { "ActorUserId", "Type", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueTimelineEvents_CommentId",
                table: "IssueTimelineEvents",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueTimelineEvents_IssueId_CreatedAtUtc_Id",
                table: "IssueTimelineEvents",
                columns: new[] { "IssueId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyRepositoryRoutes_NormalizedProject",
                table: "LegacyRepositoryRoutes",
                column: "NormalizedProject",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyRepositoryRoutes_RepositoryId",
                table: "LegacyRepositoryRoutes",
                column: "RepositoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceAliases_ExpiresAtUtc",
                table: "NamespaceAliases",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceAliases_NamespaceId_CreatedAtUtc",
                table: "NamespaceAliases",
                columns: new[] { "NamespaceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceClaims_NamespaceAliasId",
                table: "NamespaceClaims",
                column: "NamespaceAliasId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NamespaceClaims_NamespaceId",
                table: "NamespaceClaims",
                column: "NamespaceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Namespaces_NormalizedSlug",
                table: "Namespaces",
                column: "NormalizedSlug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Namespaces_TeamId",
                table: "Namespaces",
                column: "TeamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Namespaces_UserId",
                table: "Namespaces",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewComments_AuthorUserId",
                table: "PullRequestReviewComments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewComments_ThreadId_CreatedAtUtc_Id",
                table: "PullRequestReviewComments",
                columns: new[] { "ThreadId", "CreatedAtUtc", "Id" });

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

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewThreads_AuthorUserId",
                table: "PullRequestReviewThreads",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewThreads_PullRequestId_CreatedAtUtc_Id",
                table: "PullRequestReviewThreads",
                columns: new[] { "PullRequestId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewThreads_PullRequestId_Status",
                table: "PullRequestReviewThreads",
                columns: new[] { "PullRequestId", "IsOutdated", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestReviewThreads_ResolvedByUserId",
                table: "PullRequestReviewThreads",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_AssigneeUserId",
                table: "PullRequests",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_AuthorUserId",
                table: "PullRequests",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_MergedByUserId",
                table: "PullRequests",
                column: "MergedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_RepositoryId_ActivePairKey",
                table: "PullRequests",
                columns: new[] { "RepositoryId", "ActivePairKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_RepositoryId_Number",
                table: "PullRequests",
                columns: new[] { "RepositoryId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_RepositoryId_State_UpdatedAtUtc",
                table: "PullRequests",
                columns: new[] { "RepositoryId", "State", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_SourceRepositoryId",
                table: "PullRequests",
                column: "SourceRepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestTimelineEvents_ActorUserId",
                table: "PullRequestTimelineEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestTimelineEvents_PullRequestId_CreatedAtUtc",
                table: "PullRequestTimelineEvents",
                columns: new[] { "PullRequestId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RenameEvents_Subject_Window",
                table: "RenameEvents",
                columns: new[] { "SubjectType", "SubjectId", "EventType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_ForkedFromRepositoryId",
                table: "Repositories",
                column: "ForkedFromRepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_ForkNetworkRootRepositoryId",
                table: "Repositories",
                column: "ForkNetworkRootRepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_NamespaceId_NormalizedName",
                table: "Repositories",
                columns: new[] { "NamespaceId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_StorageName",
                table: "Repositories",
                column: "StorageName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAliases_ExpiresAtUtc",
                table: "RepositoryAliases",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAliases_NamespaceId",
                table: "RepositoryAliases",
                column: "NamespaceId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryAliases_RepositoryId_CreatedAtUtc",
                table: "RepositoryAliases",
                columns: new[] { "RepositoryId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryClaims_RepositoryAliasId",
                table: "RepositoryClaims",
                column: "RepositoryAliasId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryClaims_RepositoryId",
                table: "RepositoryClaims",
                column: "RepositoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SshKeys_Fingerprint",
                table: "SshKeys",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SshKeys_UserId",
                table: "SshKeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamRepositoryRoles_RepositoryId",
                table: "TeamRepositoryRoles",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_NormalizedName",
                table: "Teams",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRepositoryRoles_RepositoryId",
                table: "UserRepositoryRoles",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTeamRoles_TeamId",
                table: "UserTeamRoles",
                column: "TeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

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
                name: "LegacyRepositoryRoutes");

            migrationBuilder.DropTable(
                name: "NamespaceClaims");

            migrationBuilder.DropTable(
                name: "PullRequestReviewComments");

            migrationBuilder.DropTable(
                name: "PullRequestReviewers");

            migrationBuilder.DropTable(
                name: "PullRequestReviews");

            migrationBuilder.DropTable(
                name: "PullRequestTimelineEvents");

            migrationBuilder.DropTable(
                name: "RenameEvents");

            migrationBuilder.DropTable(
                name: "RepositoryClaims");

            migrationBuilder.DropTable(
                name: "SshKeys");

            migrationBuilder.DropTable(
                name: "TeamRepositoryRoles");

            migrationBuilder.DropTable(
                name: "UserRepositoryRoles");

            migrationBuilder.DropTable(
                name: "UserTeamRoles");

            migrationBuilder.DropTable(
                name: "WorkItemSequences");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "IssueLabels");

            migrationBuilder.DropTable(
                name: "IssueComments");

            migrationBuilder.DropTable(
                name: "NamespaceAliases");

            migrationBuilder.DropTable(
                name: "PullRequestReviewThreads");

            migrationBuilder.DropTable(
                name: "RepositoryAliases");

            migrationBuilder.DropTable(
                name: "Issues");

            migrationBuilder.DropTable(
                name: "PullRequests");

            migrationBuilder.DropTable(
                name: "IssueMilestones");

            migrationBuilder.DropTable(
                name: "Repositories");

            migrationBuilder.DropTable(
                name: "Namespaces");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Teams");
        }
    }
}
