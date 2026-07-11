using GitCandy.Data.Domain;
using GitCandy.Data.Configuration;
using GitCandy.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data;

/// <summary>
/// GitCandy 面向 ASP.NET Core Identity 和新领域模型的 EF Core 上下文。
/// </summary>
public sealed class GitCandyDbContext : IdentityDbContext<GitCandyUser>
{
    /// <summary>
    /// 初始化 GitCandy 数据上下文，并禁用 EF Core lazy loading。
    /// </summary>
    /// <param name="options">EF Core 上下文选项。</param>
    public GitCandyDbContext(DbContextOptions<GitCandyDbContext> options)
        : base(options)
    {
        ChangeTracker.LazyLoadingEnabled = false;
    }

    /// <summary>
    /// GitCandy 仓库领域表。
    /// </summary>
    public DbSet<GitCandyRepository> Repositories => Set<GitCandyRepository>();

    /// <summary>
    /// GitCandy 团队领域表。
    /// </summary>
    public DbSet<GitCandyTeam> Teams => Set<GitCandyTeam>();

    /// <summary>
    /// 用户仓库角色领域表。
    /// </summary>
    public DbSet<GitCandyUserRepositoryRole> UserRepositoryRoles => Set<GitCandyUserRepositoryRole>();

    /// <summary>
    /// 团队仓库角色领域表。
    /// </summary>
    public DbSet<GitCandyTeamRepositoryRole> TeamRepositoryRoles => Set<GitCandyTeamRepositoryRole>();

    /// <summary>
    /// 用户团队角色领域表。
    /// </summary>
    public DbSet<GitCandyUserTeamRole> UserTeamRoles => Set<GitCandyUserTeamRole>();

    /// <summary>
    /// SSH 公钥领域表。
    /// </summary>
    public DbSet<GitCandySshKey> SshKeys => Set<GitCandySshKey>();

    /// <summary>稳定用户/团队 namespace。</summary>
    public DbSet<GitCandyNamespace> Namespaces => Set<GitCandyNamespace>();

    /// <summary>namespace 历史 alias。</summary>
    public DbSet<GitCandyNamespaceAlias> NamespaceAliases => Set<GitCandyNamespaceAlias>();

    /// <summary>仓库历史 alias。</summary>
    public DbSet<GitCandyRepositoryAlias> RepositoryAliases => Set<GitCandyRepositoryAlias>();

    /// <summary>全局 namespace 名称占用。</summary>
    public DbSet<GitCandyNamespaceClaim> NamespaceClaims => Set<GitCandyNamespaceClaim>();

    /// <summary>namespace 内仓库名称占用。</summary>
    public DbSet<GitCandyRepositoryClaim> RepositoryClaims => Set<GitCandyRepositoryClaim>();

    /// <summary>名称生命周期审计事件。</summary>
    public DbSet<GitCandyRenameEvent> RenameEvents => Set<GitCandyRenameEvent>();

    /// <summary>旧 Git 项目地址映射。</summary>
    public DbSet<GitCandyLegacyRepositoryRoute> LegacyRepositoryRoutes => Set<GitCandyLegacyRepositoryRoute>();

    public DbSet<GitCandyWorkItemSequence> WorkItemSequences => Set<GitCandyWorkItemSequence>();
    public DbSet<GitCandyIssue> Issues => Set<GitCandyIssue>();
    public DbSet<GitCandyIssueComment> IssueComments => Set<GitCandyIssueComment>();
    public DbSet<GitCandyIssueEdit> IssueEdits => Set<GitCandyIssueEdit>();
    public DbSet<GitCandyIssueTimelineEvent> IssueTimelineEvents => Set<GitCandyIssueTimelineEvent>();
    public DbSet<GitCandyIssueLabel> IssueLabels => Set<GitCandyIssueLabel>();
    public DbSet<GitCandyIssueLabelLink> IssueLabelLinks => Set<GitCandyIssueLabelLink>();
    public DbSet<GitCandyIssueMilestone> IssueMilestones => Set<GitCandyIssueMilestone>();
    public DbSet<GitCandyIssueSubscription> IssueSubscriptions => Set<GitCandyIssueSubscription>();
    public DbSet<GitCandyIssueNotification> IssueNotifications => Set<GitCandyIssueNotification>();
    public DbSet<GitCandyIssueRelation> IssueRelations => Set<GitCandyIssueRelation>();
    public DbSet<GitCandyIssueReference> IssueReferences => Set<GitCandyIssueReference>();

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeDomainNames();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        NormalizeDomainNames();

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        builder.ConfigureNamespaceModel();
        builder.ConfigureIssueModel();

        builder.Entity<GitCandyUser>(entity =>
        {
            entity.Property(user => user.Id)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityKey);

            entity.Property(user => user.DisplayName)
                .HasMaxLength(SchemaLimits.UserDisplayName);

            entity.Property(user => user.Description)
                .HasMaxLength(SchemaLimits.UserDescription);
        });

        builder.Entity<IdentityRole>(entity =>
        {
            entity.Property(role => role.Id)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityKey);
        });

        builder.Entity<IdentityRoleClaim<string>>(entity =>
        {
            entity.Property(claim => claim.RoleId)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityKey);
        });

        builder.Entity<IdentityUserClaim<string>>(entity =>
        {
            entity.Property(claim => claim.UserId)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityKey);
        });

        builder.Entity<IdentityUserLogin<string>>(entity =>
        {
            entity.Property(login => login.LoginProvider)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityStoreKey);

            entity.Property(login => login.ProviderKey)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityStoreKey);

            entity.Property(login => login.UserId)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityKey);
        });

        builder.Entity<IdentityUserRole<string>>(entity =>
        {
            entity.Property(role => role.UserId)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityKey);

            entity.Property(role => role.RoleId)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityKey);
        });

        builder.Entity<IdentityUserToken<string>>(entity =>
        {
            entity.Property(token => token.UserId)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityKey);

            entity.Property(token => token.LoginProvider)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityStoreKey);

            entity.Property(token => token.Name)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityStoreKey);
        });

        builder.Entity<GitCandyRepository>(entity =>
        {
            entity.ToTable("Repositories");

            entity.HasKey(repository => repository.Id);

            entity.Property(repository => repository.Id)
                .ValueGeneratedOnAdd();

            entity.Property(repository => repository.StorageName)
                .IsRequired()
                .HasMaxLength(SchemaLimits.RepositoryStorageName);

            entity.Property(repository => repository.Name)
                .IsRequired()
                .HasMaxLength(SchemaLimits.RepositoryName);

            entity.Property(repository => repository.NormalizedName)
                .IsRequired()
                .HasMaxLength(SchemaLimits.RepositoryName);

            entity.Property(repository => repository.Description)
                .IsRequired()
                .HasMaxLength(SchemaLimits.RepositoryDescription);

            entity.Property(repository => repository.CreatedAtUtc)
                .IsRequired();

            entity.Property(repository => repository.IsPrivate)
                .IsRequired();

            entity.Property(repository => repository.AllowAnonymousRead)
                .IsRequired();

            entity.Property(repository => repository.AllowAnonymousWrite)
                .IsRequired();

            entity.Property(repository => repository.ForkedFromRepository)
                .HasMaxLength(SchemaLimits.RepositoryName);

            entity.Property(repository => repository.ForkNetworkRoot)
                .HasMaxLength(SchemaLimits.RepositoryName);

            entity.HasOne(repository => repository.Namespace)
                .WithMany(item => item.Repositories)
                .HasForeignKey(repository => repository.NamespaceId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(repository => new { repository.NamespaceId, repository.NormalizedName })
                .HasDatabaseName("IX_Repositories_NamespaceId_NormalizedName")
                .IsUnique();

            entity.HasIndex(repository => repository.StorageName)
                .HasDatabaseName("IX_Repositories_StorageName")
                .IsUnique();
        });

        builder.Entity<GitCandyTeam>(entity =>
        {
            entity.ToTable("Teams");

            entity.HasKey(team => team.Id);

            entity.Property(team => team.Id)
                .ValueGeneratedOnAdd();

            entity.Property(team => team.Name)
                .IsRequired()
                .HasMaxLength(SchemaLimits.TeamName);

            entity.Property(team => team.DisplayName)
                .IsRequired()
                .HasMaxLength(SchemaLimits.UserDisplayName);

            entity.Property(team => team.NormalizedName)
                .IsRequired()
                .HasMaxLength(SchemaLimits.TeamName);

            entity.Property(team => team.Description)
                .IsRequired()
                .HasMaxLength(SchemaLimits.TeamDescription);

            entity.Property(team => team.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(team => team.NormalizedName)
                .HasDatabaseName("IX_Teams_NormalizedName")
                .IsUnique();
        });

        builder.Entity<GitCandyUserRepositoryRole>(entity =>
        {
            entity.ToTable("UserRepositoryRoles");

            entity.HasKey(role => new { role.UserId, role.RepositoryId });

            entity.Property(role => role.UserId)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityKey);

            entity.Property(role => role.RepositoryId)
                .IsRequired();

            entity.Property(role => role.AllowRead)
                .IsRequired();

            entity.Property(role => role.AllowWrite)
                .IsRequired();

            entity.Property(role => role.IsOwner)
                .IsRequired();

            entity.HasOne(role => role.User)
                .WithMany(user => user.RepositoryRoles)
                .HasForeignKey(role => role.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(role => role.Repository)
                .WithMany(repository => repository.UserRoles)
                .HasForeignKey(role => role.RepositoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(role => role.RepositoryId)
                .HasDatabaseName("IX_UserRepositoryRoles_RepositoryId");
        });

        builder.Entity<GitCandyTeamRepositoryRole>(entity =>
        {
            entity.ToTable("TeamRepositoryRoles");

            entity.HasKey(role => new { role.TeamId, role.RepositoryId });

            entity.Property(role => role.TeamId)
                .IsRequired();

            entity.Property(role => role.RepositoryId)
                .IsRequired();

            entity.Property(role => role.AllowRead)
                .IsRequired();

            entity.Property(role => role.AllowWrite)
                .IsRequired();

            entity.HasOne(role => role.Team)
                .WithMany(team => team.RepositoryRoles)
                .HasForeignKey(role => role.TeamId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(role => role.Repository)
                .WithMany(repository => repository.TeamRoles)
                .HasForeignKey(role => role.RepositoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(role => role.RepositoryId)
                .HasDatabaseName("IX_TeamRepositoryRoles_RepositoryId");
        });

        builder.Entity<GitCandyUserTeamRole>(entity =>
        {
            entity.ToTable("UserTeamRoles");

            entity.HasKey(role => new { role.UserId, role.TeamId });

            entity.Property(role => role.UserId)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityKey);

            entity.Property(role => role.TeamId)
                .IsRequired();

            entity.Property(role => role.IsAdministrator)
                .IsRequired();

            entity.HasOne(role => role.User)
                .WithMany(user => user.TeamRoles)
                .HasForeignKey(role => role.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(role => role.Team)
                .WithMany(team => team.UserRoles)
                .HasForeignKey(role => role.TeamId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(role => role.TeamId)
                .HasDatabaseName("IX_UserTeamRoles_TeamId");
        });

        builder.Entity<GitCandySshKey>(entity =>
        {
            entity.ToTable("SshKeys");

            entity.HasKey(sshKey => sshKey.Id);

            entity.Property(sshKey => sshKey.Id)
                .ValueGeneratedOnAdd();

            entity.Property(sshKey => sshKey.UserId)
                .IsRequired()
                .HasMaxLength(SchemaLimits.IdentityKey);

            entity.Property(sshKey => sshKey.KeyType)
                .IsRequired()
                .HasMaxLength(SchemaLimits.SshKeyType);

            entity.Property(sshKey => sshKey.Fingerprint)
                .IsRequired()
                .IsFixedLength()
                .HasMaxLength(SchemaLimits.SshFingerprint);

            entity.Property(sshKey => sshKey.PublicKey)
                .IsRequired()
                .HasMaxLength(SchemaLimits.SshPublicKey);

            entity.Property(sshKey => sshKey.ImportedAtUtc)
                .IsRequired();

            entity.Property(sshKey => sshKey.LastUsedAtUtc);

            entity.HasOne(sshKey => sshKey.User)
                .WithMany(user => user.SshKeys)
                .HasForeignKey(sshKey => sshKey.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(sshKey => sshKey.UserId)
                .HasDatabaseName("IX_SshKeys_UserId");

            entity.HasIndex(sshKey => sshKey.Fingerprint)
                .HasDatabaseName("IX_SshKeys_Fingerprint")
                .IsUnique();
        });
    }

    private void NormalizeDomainNames()
    {
        foreach (var entry in ChangeTracker.Entries<GitCandyRepository>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.NormalizedName = GitCandyNameNormalizer.NormalizeRepositoryName(entry.Entity.Name);
                if (string.IsNullOrWhiteSpace(entry.Entity.StorageName))
                {
                    entry.Entity.StorageName = entry.Entity.Name.Trim();
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<GitCandyTeam>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.NormalizedName = GitCandyNameNormalizer.NormalizeTeamName(entry.Entity.Name);
                if (string.IsNullOrWhiteSpace(entry.Entity.DisplayName))
                {
                    entry.Entity.DisplayName = entry.Entity.Name.Trim();
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<GitCandyNamespace>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.NormalizedSlug = GitCandy.Application.NamespaceSlugRules.Normalize(entry.Entity.Slug);
            }
        }

        foreach (var entry in ChangeTracker.Entries<GitCandyNamespaceAlias>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.NormalizedSlug = GitCandy.Application.NamespaceSlugRules.Normalize(entry.Entity.Slug);
            }
        }

        foreach (var entry in ChangeTracker.Entries<GitCandyRepositoryAlias>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.NormalizedSlug = GitCandy.Application.NamespaceSlugRules.Normalize(entry.Entity.Slug);
            }
        }
    }
}
