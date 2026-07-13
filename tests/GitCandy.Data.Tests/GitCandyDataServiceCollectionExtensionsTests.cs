using System.Data;
using GitCandy.Data.Configuration;
using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Data.Permissions;
using GitCandy.Data.Sqlite;
using GitCandy.Teams;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitCandy.Data.Tests;

[TestClass]
public sealed class GitCandyDataServiceCollectionExtensionsTests
{
    private const string SshFingerprint = "AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99";
    private const string SshPublicKey = "AAAAB3NzaC1yc2EAAAADAQABAAABAQCgitcandytests";

    [TestMethod]
    public async Task AddGitCandyData_WithSqliteMigrations_CreatesIdentityAndDomainSchema()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            $"{Guid.NewGuid():N}.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = serviceProvider.CreateAsyncScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.MigrateAsync();

            var tables = await ReadSqliteNamesAsync(
                dbContext,
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                """);

            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "__EFMigrationsHistory",
                    "AspNetUsers",
                    "AspNetRoles",
                    "AspNetRoleClaims",
                    "AspNetUserClaims",
                    "AspNetUserLogins",
                    "AspNetUserRoles",
                    "AspNetUserTokens",
                    "Repositories",
                    "Teams",
                    "UserRepositoryRoles",
                    "TeamRepositoryRoles",
                    "UserTeamRoles",
                    "SshKeys",
                    "Namespaces",
                    "NamespaceAliases",
                    "RepositoryAliases",
                    "NamespaceClaims",
                    "RepositoryClaims",
                    "RenameEvents",
                    "LegacyRepositoryRoutes"
                },
                tables.ToList());

            CollectionAssert.DoesNotContain(tables.ToList(), "Users");
            CollectionAssert.DoesNotContain(tables.ToList(), "AuthorizationLog");

            var indexes = await ReadSqliteNamesAsync(
                dbContext,
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'index'
                """);

            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "EmailIndex",
                    "UserNameIndex",
                    "RoleNameIndex",
                    "IX_Repositories_NamespaceId_NormalizedName",
                    "IX_Repositories_StorageName",
                    "IX_Namespaces_NormalizedSlug",
                    "IX_LegacyRepositoryRoutes_NormalizedProject",
                    "IX_Teams_NormalizedName",
                    "IX_SshKeys_Fingerprint",
                    "IX_SshKeys_UserId",
                    "IX_UserRepositoryRoles_RepositoryId",
                    "IX_TeamRepositoryRoles_RepositoryId",
                    "IX_UserTeamRoles_TeamId_Role"
                },
                indexes.ToList());

            var migrations = await ReadSqliteNamesAsync(
                dbContext,
                """
                SELECT MigrationId
                FROM __EFMigrationsHistory
                """);

            Assert.IsTrue(
                migrations.Any(static migration =>
                    migration.EndsWith("_InitialIdentitySchema", StringComparison.Ordinal)),
                "SQLite schema must be created from the InitialIdentitySchema migration.");
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [TestMethod]
    public async Task AddGitCandyData_WithDomainModel_UsesExplicitConstraintsAndIndexes()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            $"{Guid.NewGuid():N}.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = serviceProvider.CreateAsyncScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.MigrateAsync();

            var model = dbContext.Model;

            AssertStringProperty(model, typeof(GitCandyUser), nameof(GitCandyUser.Id), 450, isNullable: false);
            AssertStringProperty(model, typeof(GitCandyUser), nameof(GitCandyUser.DisplayName), 128, isNullable: true);
            AssertStringProperty(model, typeof(GitCandyUser), nameof(GitCandyUser.Description), 512, isNullable: true);
            AssertStringProperty(model, typeof(IdentityRole), nameof(IdentityRole.Id), 450, isNullable: false);
            AssertStringProperty(model, typeof(IdentityRoleClaim<string>), "RoleId", 450, isNullable: false);
            AssertStringProperty(model, typeof(IdentityUserClaim<string>), "UserId", 450, isNullable: false);
            AssertStringProperty(model, typeof(IdentityUserLogin<string>), "LoginProvider", 128, isNullable: false);
            AssertStringProperty(model, typeof(IdentityUserLogin<string>), "ProviderKey", 128, isNullable: false);
            AssertStringProperty(model, typeof(IdentityUserLogin<string>), "UserId", 450, isNullable: false);
            AssertStringProperty(model, typeof(IdentityUserRole<string>), "UserId", 450, isNullable: false);
            AssertStringProperty(model, typeof(IdentityUserRole<string>), "RoleId", 450, isNullable: false);
            AssertStringProperty(model, typeof(IdentityUserToken<string>), "UserId", 450, isNullable: false);
            AssertStringProperty(model, typeof(IdentityUserToken<string>), "LoginProvider", 128, isNullable: false);
            AssertStringProperty(model, typeof(IdentityUserToken<string>), "Name", 128, isNullable: false);

            AssertStringProperty(model, typeof(GitCandyRepository), nameof(GitCandyRepository.Name), 50, isNullable: false);
            AssertStringProperty(model, typeof(GitCandyRepository), nameof(GitCandyRepository.NormalizedName), 50, isNullable: false);
            AssertStringProperty(model, typeof(GitCandyRepository), nameof(GitCandyRepository.Description), 500, isNullable: false);
            AssertStringProperty(model, typeof(GitCandyTeam), nameof(GitCandyTeam.Name), 20, isNullable: false);
            AssertStringProperty(model, typeof(GitCandyTeam), nameof(GitCandyTeam.NormalizedName), 20, isNullable: false);
            AssertStringProperty(model, typeof(GitCandyTeam), nameof(GitCandyTeam.Description), 500, isNullable: false);
            AssertStringProperty(model, typeof(GitCandyUserRepositoryRole), nameof(GitCandyUserRepositoryRole.UserId), 450, isNullable: false);
            AssertStringProperty(model, typeof(GitCandyUserTeamRole), nameof(GitCandyUserTeamRole.UserId), 450, isNullable: false);
            AssertStringProperty(model, typeof(GitCandySshKey), nameof(GitCandySshKey.UserId), 450, isNullable: false);
            AssertStringProperty(model, typeof(GitCandySshKey), nameof(GitCandySshKey.KeyType), 20, isNullable: false);
            AssertStringProperty(model, typeof(GitCandySshKey), nameof(GitCandySshKey.Fingerprint), 47, isNullable: false);
            AssertStringProperty(model, typeof(GitCandySshKey), nameof(GitCandySshKey.PublicKey), 600, isNullable: false);

            AssertPrimaryKey(model, typeof(GitCandyRepository), nameof(GitCandyRepository.Id));
            AssertPrimaryKey(model, typeof(GitCandyTeam), nameof(GitCandyTeam.Id));
            AssertPrimaryKey(model, typeof(GitCandySshKey), nameof(GitCandySshKey.Id));
            AssertPrimaryKey(
                model,
                typeof(GitCandyUserRepositoryRole),
                nameof(GitCandyUserRepositoryRole.UserId),
                nameof(GitCandyUserRepositoryRole.RepositoryId));
            AssertPrimaryKey(
                model,
                typeof(GitCandyTeamRepositoryRole),
                nameof(GitCandyTeamRepositoryRole.TeamId),
                nameof(GitCandyTeamRepositoryRole.RepositoryId));
            AssertPrimaryKey(
                model,
                typeof(GitCandyUserTeamRole),
                nameof(GitCandyUserTeamRole.UserId),
                nameof(GitCandyUserTeamRole.TeamId));

            AssertUniqueIndex(
                model,
                typeof(GitCandyRepository),
                "IX_Repositories_NamespaceId_NormalizedName",
                nameof(GitCandyRepository.NamespaceId),
                nameof(GitCandyRepository.NormalizedName));
            AssertUniqueIndex(model, typeof(GitCandyRepository), "IX_Repositories_StorageName", nameof(GitCandyRepository.StorageName));
            AssertUniqueIndex(model, typeof(GitCandyNamespace), "IX_Namespaces_NormalizedSlug", nameof(GitCandyNamespace.NormalizedSlug));
            AssertUniqueIndex(model, typeof(GitCandyTeam), "IX_Teams_NormalizedName", nameof(GitCandyTeam.NormalizedName));
            AssertUniqueIndex(model, typeof(GitCandySshKey), "IX_SshKeys_Fingerprint", nameof(GitCandySshKey.Fingerprint));
            AssertUniqueIndex(model, typeof(GitCandyUser), "UserNameIndex", nameof(GitCandyUser.NormalizedUserName));
            AssertUniqueIndex(model, typeof(IdentityRole), "RoleNameIndex", nameof(IdentityRole.NormalizedName));

            AssertForeignKey(
                model,
                typeof(GitCandyUserRepositoryRole),
                typeof(GitCandyUser),
                DeleteBehavior.Cascade,
                nameof(GitCandyUserRepositoryRole.UserId));
            AssertForeignKey(
                model,
                typeof(GitCandyUserRepositoryRole),
                typeof(GitCandyRepository),
                DeleteBehavior.Cascade,
                nameof(GitCandyUserRepositoryRole.RepositoryId));
            AssertForeignKey(
                model,
                typeof(GitCandyTeamRepositoryRole),
                typeof(GitCandyTeam),
                DeleteBehavior.Cascade,
                nameof(GitCandyTeamRepositoryRole.TeamId));
            AssertForeignKey(
                model,
                typeof(GitCandyTeamRepositoryRole),
                typeof(GitCandyRepository),
                DeleteBehavior.Cascade,
                nameof(GitCandyTeamRepositoryRole.RepositoryId));
            AssertForeignKey(
                model,
                typeof(GitCandyUserTeamRole),
                typeof(GitCandyUser),
                DeleteBehavior.Cascade,
                nameof(GitCandyUserTeamRole.UserId));
            AssertForeignKey(
                model,
                typeof(GitCandyUserTeamRole),
                typeof(GitCandyTeam),
                DeleteBehavior.Cascade,
                nameof(GitCandyUserTeamRole.TeamId));
            AssertForeignKey(
                model,
                typeof(GitCandySshKey),
                typeof(GitCandyUser),
                DeleteBehavior.Cascade,
                nameof(GitCandySshKey.UserId));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [TestMethod]
    public async Task AddGitCandyData_WithSqliteProvider_CreatesIdentityDatabase()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            $"{Guid.NewGuid():N}.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["GitCandy:Database:DbContextPoolSize"] = "16",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = serviceProvider.CreateAsyncScope();

            var options = scope.ServiceProvider.GetRequiredService<IOptions<GitCandyDatabaseOptions>>().Value;
            Assert.AreEqual(GitCandyDatabaseProvider.Sqlite, options.Provider);
            Assert.AreEqual(16, options.DbContextPoolSize);

            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.EnsureCreatedAsync();

            dbContext.Users.Add(new GitCandyUser
            {
                UserName = "admin",
                Email = "admin@example.com"
            });
            await dbContext.SaveChangesAsync();

            var user = await dbContext.Users.SingleAsync(item => item.UserName == "admin");

            Assert.AreEqual("admin@example.com", user.Email);
            Assert.IsTrue(File.Exists(databasePath));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [TestMethod]
    public async Task AddGitCandyData_WithDomainTables_CreatesReadsWritesAndQueriesPermissions()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            $"{Guid.NewGuid():N}.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = serviceProvider.CreateAsyncScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.MigrateAsync();

            var alice = new GitCandyUser
            {
                Id = "user-alice",
                UserName = "alice",
                NormalizedUserName = "ALICE",
                Email = "alice@example.com",
                NormalizedEmail = "ALICE@EXAMPLE.COM"
            };
            var bob = new GitCandyUser
            {
                Id = "user-bob",
                UserName = "bob",
                NormalizedUserName = "BOB",
                Email = "bob@example.com",
                NormalizedEmail = "BOB@EXAMPLE.COM"
            };
            var carol = new GitCandyUser
            {
                Id = "user-carol",
                UserName = "carol",
                NormalizedUserName = "CAROL",
                Email = "carol@example.com",
                NormalizedEmail = "CAROL@EXAMPLE.COM"
            };

            var publicRepository = new GitCandyRepository
            {
                Name = "public-demo",
                Description = "Public demo repository",
                CreatedAtUtc = DateTime.UtcNow,
                AllowAnonymousRead = true
            };
            var privateRepository = new GitCandyRepository
            {
                Name = "private-demo",
                Description = "Private demo repository",
                CreatedAtUtc = DateTime.UtcNow,
                IsPrivate = true
            };
            var coreTeam = new GitCandyTeam
            {
                Name = "core",
                Description = "Core maintainers",
                CreatedAtUtc = DateTime.UtcNow
            };
            var sshKey = new GitCandySshKey
            {
                UserId = alice.Id,
                KeyType = "ssh-rsa",
                Fingerprint = SshFingerprint,
                PublicKey = SshPublicKey,
                ImportedAtUtc = DateTime.UtcNow
            };

            dbContext.Users.AddRange(alice, bob, carol);
            dbContext.Repositories.AddRange(publicRepository, privateRepository);
            dbContext.Teams.Add(coreTeam);
            dbContext.SshKeys.Add(sshKey);
            await dbContext.SaveChangesAsync();

            dbContext.UserRepositoryRoles.Add(new GitCandyUserRepositoryRole
            {
                UserId = alice.Id,
                RepositoryId = privateRepository.Id,
                AllowRead = true,
                AllowWrite = true,
                IsOwner = true
            });
            dbContext.UserTeamRoles.Add(new GitCandyUserTeamRole
            {
                UserId = bob.Id,
                TeamId = coreTeam.Id
            });
            dbContext.TeamRepositoryRoles.Add(new GitCandyTeamRepositoryRole
            {
                TeamId = coreTeam.Id,
                RepositoryId = privateRepository.Id,
                AllowRead = true,
                AllowWrite = true
            });
            await dbContext.SaveChangesAsync();

            var savedRepository = await dbContext.Repositories
                .SingleAsync(repository => repository.Name == "private-demo");
            Assert.AreEqual("PRIVATE-DEMO", savedRepository.NormalizedName);

            var savedSshKey = await dbContext.SshKeys
                .Include(key => key.User)
                .SingleAsync(key => key.Fingerprint == SshFingerprint);
            Assert.AreEqual(alice.Id, savedSshKey.UserId);
            Assert.AreEqual("ssh-rsa", savedSshKey.KeyType);
            Assert.AreEqual(SshPublicKey, savedSshKey.PublicKey);
            Assert.IsNotNull(savedSshKey.User);
            Assert.AreEqual("alice", savedSshKey.User.UserName);
            Assert.IsNull(savedSshKey.LastUsedAtUtc);

            var permissionQuery = scope.ServiceProvider.GetRequiredService<IGitCandyRepositoryPermissionQuery>();

            Assert.IsTrue(await permissionQuery.CanReadRepositoryAsync("PUBLIC-DEMO", null, isAdministrator: false));
            Assert.IsFalse(await permissionQuery.CanWriteRepositoryAsync("public-demo", null, isAdministrator: false));
            Assert.IsTrue(await permissionQuery.CanReadRepositoryAsync("private-demo", alice.Id, isAdministrator: false));
            Assert.IsTrue(await permissionQuery.CanWriteRepositoryAsync("private-demo", alice.Id, isAdministrator: false));
            Assert.IsTrue(await permissionQuery.CanReadRepositoryAsync("private-demo", bob.Id, isAdministrator: false));
            Assert.IsTrue(await permissionQuery.CanWriteRepositoryAsync("private-demo", bob.Id, isAdministrator: false));
            Assert.IsFalse(await permissionQuery.CanReadRepositoryAsync("private-demo", carol.Id, isAdministrator: false));
            Assert.IsTrue(await permissionQuery.CanReadRepositoryAsync("PRIVATE-DEMO", carol.Id, isAdministrator: true));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [TestMethod]
    public async Task AddGitCandyData_WithDuplicateSshFingerprint_RejectsDuplicateKey()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            $"{Guid.NewGuid():N}.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = serviceProvider.CreateAsyncScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.MigrateAsync();

            dbContext.Users.AddRange(
                new GitCandyUser
                {
                    Id = "user-alice",
                    UserName = "alice",
                    NormalizedUserName = "ALICE"
                },
                new GitCandyUser
                {
                    Id = "user-bob",
                    UserName = "bob",
                    NormalizedUserName = "BOB"
                });

            dbContext.SshKeys.AddRange(
                new GitCandySshKey
                {
                    UserId = "user-alice",
                    KeyType = "ssh-rsa",
                    Fingerprint = SshFingerprint,
                    PublicKey = SshPublicKey,
                    ImportedAtUtc = DateTime.UtcNow
                },
                new GitCandySshKey
                {
                    UserId = "user-bob",
                    KeyType = "ssh-rsa",
                    Fingerprint = SshFingerprint,
                    PublicKey = SshPublicKey,
                    ImportedAtUtc = DateTime.UtcNow
                });

            try
            {
                await dbContext.SaveChangesAsync();
                Assert.Fail("Duplicate SSH fingerprints must be rejected by the domain schema.");
            }
            catch (DbUpdateException)
            {
            }
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [TestMethod]
    public async Task AddGitCandyData_WithCaseOnlyRepositoryAndTeamNames_RejectsDuplicates()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            $"{Guid.NewGuid():N}.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
                await dbContext.Database.MigrateAsync();
            }

            await SaveRepositoryAsync(serviceProvider, "sample-demo");
            await SaveTeamAsync(serviceProvider, "core");

            await Assert.ThrowsExactlyAsync<DbUpdateException>(
                () => SaveRepositoryAsync(serviceProvider, "SAMPLE-DEMO"));
            await Assert.ThrowsExactlyAsync<DbUpdateException>(
                () => SaveTeamAsync(serviceProvider, "CORE"));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [TestMethod]
    public async Task AddGitCandyData_WithDomainUserForeignKeys_UsesIdentityUserIds()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            $"{Guid.NewGuid():N}.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = serviceProvider.CreateAsyncScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.MigrateAsync();

            await AssertHasIdentityUserForeignKeyAsync(dbContext, "UserRepositoryRoles");
            await AssertHasIdentityUserForeignKeyAsync(dbContext, "UserTeamRoles");
            await AssertHasIdentityUserForeignKeyAsync(dbContext, "SshKeys");

            var repository = new GitCandyRepository
            {
                Name = "private-demo",
                Description = "Private demo repository",
                CreatedAtUtc = DateTime.UtcNow,
                IsPrivate = true
            };
            var team = new GitCandyTeam
            {
                Name = "core",
                Description = "Core maintainers",
                CreatedAtUtc = DateTime.UtcNow
            };

            dbContext.Repositories.Add(repository);
            dbContext.Teams.Add(team);
            await dbContext.SaveChangesAsync();

            await Assert.ThrowsExactlyAsync<DbUpdateException>(
                () => SaveUserRepositoryRoleWithMissingUserAsync(serviceProvider, repository.Id));
            await Assert.ThrowsExactlyAsync<DbUpdateException>(
                () => SaveUserTeamRoleWithMissingUserAsync(serviceProvider, team.Id));
            await Assert.ThrowsExactlyAsync<DbUpdateException>(
                () => SaveSshKeyWithMissingUserAsync(serviceProvider));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [TestMethod]
    public async Task AddGitCandyData_WithDeletedIdentityUser_CascadesDomainUserForeignKeys()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "GitCandy.Tests",
            $"{Guid.NewGuid():N}.db");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["GitCandy:Database:Provider"] = "sqlite",
                    ["ConnectionStrings:GitCandy"] = $"Data Source={databasePath};Pooling=False"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddGitCandyData(configuration, builder => builder.AddSqlite());

            await using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
            await using var scope = serviceProvider.CreateAsyncScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();
            await dbContext.Database.MigrateAsync();

            var alice = new GitCandyUser
            {
                Id = "user-alice",
                UserName = "alice",
                NormalizedUserName = "ALICE",
                Email = "alice@example.com",
                NormalizedEmail = "ALICE@EXAMPLE.COM"
            };
            var repository = new GitCandyRepository
            {
                Name = "private-demo",
                Description = "Private demo repository",
                CreatedAtUtc = DateTime.UtcNow,
                IsPrivate = true
            };
            var team = new GitCandyTeam
            {
                Name = "core",
                Description = "Core maintainers",
                CreatedAtUtc = DateTime.UtcNow
            };

            dbContext.Users.Add(alice);
            dbContext.Repositories.Add(repository);
            dbContext.Teams.Add(team);
            await dbContext.SaveChangesAsync();

            dbContext.UserRepositoryRoles.Add(new GitCandyUserRepositoryRole
            {
                UserId = alice.Id,
                RepositoryId = repository.Id,
                AllowRead = true,
                AllowWrite = true,
                IsOwner = true
            });
            dbContext.UserTeamRoles.Add(new GitCandyUserTeamRole
            {
                UserId = alice.Id,
                TeamId = team.Id,
                Role = TeamRole.TeamOwner
            });
            dbContext.SshKeys.Add(new GitCandySshKey
            {
                UserId = alice.Id,
                KeyType = "ssh-rsa",
                Fingerprint = SshFingerprint,
                PublicKey = SshPublicKey,
                ImportedAtUtc = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync();

            dbContext.ChangeTracker.Clear();

            var savedUser = await dbContext.Users.SingleAsync(user => user.Id == alice.Id);
            dbContext.Users.Remove(savedUser);
            await dbContext.SaveChangesAsync();

            Assert.AreEqual(0, await dbContext.UserRepositoryRoles.CountAsync());
            Assert.AreEqual(0, await dbContext.UserTeamRoles.CountAsync());
            Assert.AreEqual(0, await dbContext.SshKeys.CountAsync());
            Assert.AreEqual(1, await dbContext.Repositories.CountAsync());
            Assert.AreEqual(1, await dbContext.Teams.CountAsync());
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [TestMethod]
    public void AddGitCandyData_WithUnregisteredProvider_ThrowsNotSupportedException()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitCandy:Database:Provider"] = "pgsql",
                ["ConnectionStrings:GitCandy"] = "Host=localhost;Database=GitCandy;Username=postgres;Password=postgres"
            })
            .Build();

        var services = new ServiceCollection();

        Assert.ThrowsExactly<NotSupportedException>(
            () => services.AddGitCandyData(configuration, builder => builder.AddSqlite()));
    }

    [TestMethod]
    public void AddGitCandyData_WithUnsupportedProvider_ThrowsNotSupportedException()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataBase"] = "mysql",
                ["ConnectionStrings:GitCandy"] = "Server=localhost;Database=GitCandy"
            })
            .Build();

        var services = new ServiceCollection();

        Assert.ThrowsExactly<NotSupportedException>(
            () => services.AddGitCandyData(configuration, builder => builder.AddSqlite()));
    }

    private static async Task<IReadOnlySet<string>> ReadSqliteNamesAsync(
        GitCandyDbContext dbContext,
        string commandText)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static void AssertStringProperty(
        IModel model,
        Type entityClrType,
        string propertyName,
        int maxLength,
        bool isNullable)
    {
        var property = GetEntityType(model, entityClrType).FindProperty(propertyName)
            ?? throw new AssertFailedException($"{entityClrType.Name}.{propertyName} was not found.");

        Assert.AreEqual(typeof(string), property.ClrType);
        Assert.AreEqual(maxLength, property.GetMaxLength(), $"{entityClrType.Name}.{propertyName} max length mismatch.");
        Assert.AreEqual(isNullable, property.IsNullable, $"{entityClrType.Name}.{propertyName} nullability mismatch.");
    }

    private static void AssertPrimaryKey(IModel model, Type entityClrType, params string[] propertyNames)
    {
        var primaryKey = GetEntityType(model, entityClrType).FindPrimaryKey()
            ?? throw new AssertFailedException($"{entityClrType.Name} primary key was not found.");

        CollectionAssert.AreEqual(propertyNames, primaryKey.Properties.Select(static property => property.Name).ToArray());
    }

    private static void AssertUniqueIndex(
        IModel model,
        Type entityClrType,
        string databaseName,
        params string[] propertyNames)
    {
        var index = GetEntityType(model, entityClrType)
            .GetIndexes()
            .SingleOrDefault(index => string.Equals(index.GetDatabaseName(), databaseName, StringComparison.Ordinal));

        if (index is null)
        {
            throw new AssertFailedException($"{entityClrType.Name}.{databaseName} was not found.");
        }

        Assert.IsTrue(index.IsUnique, $"{entityClrType.Name}.{databaseName} must be unique.");
        CollectionAssert.AreEqual(propertyNames, index.Properties.Select(static property => property.Name).ToArray());
    }

    private static void AssertForeignKey(
        IModel model,
        Type entityClrType,
        Type principalClrType,
        DeleteBehavior deleteBehavior,
        params string[] propertyNames)
    {
        var foreignKey = GetEntityType(model, entityClrType)
            .GetForeignKeys()
            .SingleOrDefault(foreignKey =>
                foreignKey.PrincipalEntityType.ClrType == principalClrType
                && propertyNames.SequenceEqual(foreignKey.Properties.Select(static property => property.Name)));

        if (foreignKey is null)
        {
            throw new AssertFailedException(
                $"{entityClrType.Name} foreign key to {principalClrType.Name} over {string.Join(", ", propertyNames)} was not found.");
        }

        Assert.IsTrue(foreignKey.IsRequired, $"{entityClrType.Name} foreign key to {principalClrType.Name} must be required.");
        Assert.AreEqual(deleteBehavior, foreignKey.DeleteBehavior);
    }

    private static IEntityType GetEntityType(IModel model, Type entityClrType)
    {
        return model.FindEntityType(entityClrType)
            ?? throw new AssertFailedException($"{entityClrType.Name} entity type was not found.");
    }

    private static async Task AssertHasIdentityUserForeignKeyAsync(
        GitCandyDbContext dbContext,
        string tableName)
    {
        var foreignKeys = await ReadSqliteForeignKeysAsync(dbContext, tableName);

        Assert.IsTrue(
            foreignKeys.Any(static foreignKey =>
                string.Equals(foreignKey.PrincipalTable, "AspNetUsers", StringComparison.OrdinalIgnoreCase)
                && string.Equals(foreignKey.ForeignKeyColumn, "UserId", StringComparison.OrdinalIgnoreCase)
                && string.Equals(foreignKey.PrincipalColumn, "Id", StringComparison.OrdinalIgnoreCase)
                && string.Equals(foreignKey.OnDelete, "CASCADE", StringComparison.OrdinalIgnoreCase)),
            $"{tableName}.UserId must reference AspNetUsers.Id with cascade delete.");
    }

    private static async Task<IReadOnlyList<SqliteForeignKey>> ReadSqliteForeignKeysAsync(
        GitCandyDbContext dbContext,
        string tableName)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list('{tableName.Replace("'", "''", StringComparison.Ordinal)}')";

        var foreignKeys = new List<SqliteForeignKey>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            foreignKeys.Add(new SqliteForeignKey(
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(6)));
        }

        return foreignKeys;
    }

    private static async Task SaveUserRepositoryRoleWithMissingUserAsync(
        IServiceProvider serviceProvider,
        long repositoryId)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();

        dbContext.UserRepositoryRoles.Add(new GitCandyUserRepositoryRole
        {
            UserId = "missing-identity-user",
            RepositoryId = repositoryId,
            AllowRead = true
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task SaveUserTeamRoleWithMissingUserAsync(
        IServiceProvider serviceProvider,
        long teamId)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();

        dbContext.UserTeamRoles.Add(new GitCandyUserTeamRole
        {
            UserId = "missing-identity-user",
            TeamId = teamId
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task SaveSshKeyWithMissingUserAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();

        dbContext.SshKeys.Add(new GitCandySshKey
        {
            UserId = "missing-identity-user",
            KeyType = "ssh-rsa",
            Fingerprint = "11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00",
            PublicKey = SshPublicKey,
            ImportedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task SaveRepositoryAsync(IServiceProvider serviceProvider, string repositoryName)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();

        dbContext.Repositories.Add(new GitCandyRepository
        {
            Name = repositoryName,
            Description = "Repository name uniqueness sample",
            CreatedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    private static async Task SaveTeamAsync(IServiceProvider serviceProvider, string teamName)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GitCandyDbContext>();

        dbContext.Teams.Add(new GitCandyTeam
        {
            Name = teamName,
            Description = "Team name uniqueness sample",
            CreatedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    private sealed record SqliteForeignKey(
        string PrincipalTable,
        string ForeignKeyColumn,
        string PrincipalColumn,
        string OnDelete);
}
