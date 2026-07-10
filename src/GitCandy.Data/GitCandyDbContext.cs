using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data;

/// <summary>
/// GitCandy 面向 ASP.NET Core Identity 和新领域模型的 EF Core 上下文。
/// </summary>
public sealed class GitCandyDbContext(DbContextOptions<GitCandyDbContext> options)
    : IdentityDbContext<GitCandyUser>(options)
{
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

            entity.HasIndex(repository => repository.NormalizedName)
                .HasDatabaseName("IX_Repositories_NormalizedName")
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
            }
        }

        foreach (var entry in ChangeTracker.Entries<GitCandyTeam>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.NormalizedName = GitCandyNameNormalizer.NormalizeTeamName(entry.Entity.Name);
            }
        }
    }
}
