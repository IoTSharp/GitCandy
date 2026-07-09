using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
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
            entity.Property(user => user.DisplayName)
                .HasMaxLength(128);

            entity.Property(user => user.Description)
                .HasMaxLength(512);
        });

        builder.Entity<GitCandyRepository>(entity =>
        {
            entity.ToTable("Repositories");

            entity.HasKey(repository => repository.Id);

            entity.Property(repository => repository.Name)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(repository => repository.NormalizedName)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(repository => repository.Description)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(repository => repository.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(repository => repository.NormalizedName)
                .IsUnique();
        });

        builder.Entity<GitCandyTeam>(entity =>
        {
            entity.ToTable("Teams");

            entity.HasKey(team => team.Id);

            entity.Property(team => team.Name)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(team => team.NormalizedName)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(team => team.Description)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(team => team.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(team => team.NormalizedName)
                .IsUnique();
        });

        builder.Entity<GitCandyUserRepositoryRole>(entity =>
        {
            entity.ToTable("UserRepositoryRoles");

            entity.HasKey(role => new { role.UserId, role.RepositoryId });

            entity.HasOne(role => role.User)
                .WithMany(user => user.RepositoryRoles)
                .HasForeignKey(role => role.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(role => role.Repository)
                .WithMany(repository => repository.UserRoles)
                .HasForeignKey(role => role.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(role => role.RepositoryId);
        });

        builder.Entity<GitCandyTeamRepositoryRole>(entity =>
        {
            entity.ToTable("TeamRepositoryRoles");

            entity.HasKey(role => new { role.TeamId, role.RepositoryId });

            entity.HasOne(role => role.Team)
                .WithMany(team => team.RepositoryRoles)
                .HasForeignKey(role => role.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(role => role.Repository)
                .WithMany(repository => repository.TeamRoles)
                .HasForeignKey(role => role.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(role => role.RepositoryId);
        });

        builder.Entity<GitCandyUserTeamRole>(entity =>
        {
            entity.ToTable("UserTeamRoles");

            entity.HasKey(role => new { role.UserId, role.TeamId });

            entity.HasOne(role => role.User)
                .WithMany(user => user.TeamRoles)
                .HasForeignKey(role => role.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(role => role.Team)
                .WithMany(team => team.UserRoles)
                .HasForeignKey(role => role.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(role => role.TeamId);
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
