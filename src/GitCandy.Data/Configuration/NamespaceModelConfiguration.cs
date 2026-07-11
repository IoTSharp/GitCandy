using GitCandy.Application;
using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class NamespaceModelConfiguration
{
    public static void ConfigureNamespaceModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyNamespace>(entity =>
        {
            entity.ToTable("Namespaces");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.OwnerType).IsRequired();
            entity.Property(item => item.UserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.Slug).IsRequired().HasMaxLength(SchemaLimits.NamespaceSlug);
            entity.Property(item => item.NormalizedSlug).IsRequired().HasMaxLength(SchemaLimits.NamespaceSlug);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.Version).IsRequired().IsConcurrencyToken();
            entity.HasIndex(item => item.NormalizedSlug)
                .HasDatabaseName("IX_Namespaces_NormalizedSlug")
                .IsUnique();
            entity.HasIndex(item => item.UserId)
                .HasDatabaseName("IX_Namespaces_UserId")
                .IsUnique();
            entity.HasIndex(item => item.TeamId)
                .HasDatabaseName("IX_Namespaces_TeamId")
                .IsUnique();
            entity.HasOne(item => item.User)
                .WithOne(user => user.Namespace)
                .HasForeignKey<GitCandyNamespace>(item => item.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Team)
                .WithOne(team => team.Namespace)
                .HasForeignKey<GitCandyNamespace>(item => item.TeamId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasData(new GitCandyNamespace
            {
                Id = GitCandyNamespace.LegacyNamespaceId,
                OwnerType = NamespaceOwnerType.System,
                Slug = "legacy",
                NormalizedSlug = "LEGACY",
                CreatedAtUtc = DateTime.UnixEpoch,
                Version = 0
            });
        });

        builder.Entity<GitCandyNamespaceAlias>(entity =>
        {
            entity.ToTable("NamespaceAliases");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Slug).IsRequired().HasMaxLength(SchemaLimits.NamespaceSlug);
            entity.Property(item => item.NormalizedSlug).IsRequired().HasMaxLength(SchemaLimits.NamespaceSlug);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.ExpiresAtUtc).IsRequired();
            entity.HasOne(item => item.Namespace)
                .WithMany(item => item.Aliases)
                .HasForeignKey(item => item.NamespaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.NamespaceId, item.CreatedAtUtc })
                .HasDatabaseName("IX_NamespaceAliases_NamespaceId_CreatedAtUtc");
            entity.HasIndex(item => item.ExpiresAtUtc)
                .HasDatabaseName("IX_NamespaceAliases_ExpiresAtUtc");
        });

        builder.Entity<GitCandyRepositoryAlias>(entity =>
        {
            entity.ToTable("RepositoryAliases");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Slug).IsRequired().HasMaxLength(SchemaLimits.RepositoryName);
            entity.Property(item => item.NormalizedSlug).IsRequired().HasMaxLength(SchemaLimits.RepositoryName);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.ExpiresAtUtc).IsRequired();
            entity.HasOne(item => item.Namespace)
                .WithMany()
                .HasForeignKey(item => item.NamespaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Repository)
                .WithMany(item => item.Aliases)
                .HasForeignKey(item => item.RepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.RepositoryId, item.CreatedAtUtc })
                .HasDatabaseName("IX_RepositoryAliases_RepositoryId_CreatedAtUtc");
            entity.HasIndex(item => item.ExpiresAtUtc)
                .HasDatabaseName("IX_RepositoryAliases_ExpiresAtUtc");
        });

        builder.Entity<GitCandyNamespaceClaim>(entity =>
        {
            entity.ToTable("NamespaceClaims");
            entity.HasKey(item => item.NormalizedSlug);
            entity.Property(item => item.NormalizedSlug).HasMaxLength(SchemaLimits.NamespaceSlug);
            entity.Property(item => item.Slug).IsRequired().HasMaxLength(SchemaLimits.NamespaceSlug);
            entity.Property(item => item.ClaimType).IsRequired();
            entity.HasOne(item => item.Namespace)
                .WithMany()
                .HasForeignKey(item => item.NamespaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.NamespaceAlias)
                .WithOne()
                .HasForeignKey<GitCandyNamespaceClaim>(item => item.NamespaceAliasId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.NamespaceId)
                .HasDatabaseName("IX_NamespaceClaims_NamespaceId")
                .IsUnique();
            entity.HasIndex(item => item.NamespaceAliasId)
                .HasDatabaseName("IX_NamespaceClaims_NamespaceAliasId")
                .IsUnique();

            var claims = NamespaceSlugRules.ReservedSlugs
                .Select(slug => new GitCandyNamespaceClaim
                {
                    NormalizedSlug = NamespaceSlugRules.Normalize(slug),
                    Slug = slug,
                    ClaimType = string.Equals(slug, "legacy", StringComparison.Ordinal)
                        ? NameClaimType.Current
                        : NameClaimType.Reserved,
                    NamespaceId = string.Equals(slug, "legacy", StringComparison.Ordinal)
                        ? GitCandyNamespace.LegacyNamespaceId
                        : null
                })
                .ToArray();
            entity.HasData(claims);
        });

        builder.Entity<GitCandyRepositoryClaim>(entity =>
        {
            entity.ToTable("RepositoryClaims");
            entity.HasKey(item => new { item.NamespaceId, item.NormalizedSlug });
            entity.Property(item => item.NormalizedSlug).HasMaxLength(SchemaLimits.RepositoryName);
            entity.Property(item => item.Slug).IsRequired().HasMaxLength(SchemaLimits.RepositoryName);
            entity.Property(item => item.ClaimType).IsRequired();
            entity.HasOne(item => item.Namespace)
                .WithMany()
                .HasForeignKey(item => item.NamespaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Repository)
                .WithOne()
                .HasForeignKey<GitCandyRepositoryClaim>(item => item.RepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.RepositoryAlias)
                .WithOne()
                .HasForeignKey<GitCandyRepositoryClaim>(item => item.RepositoryAliasId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.RepositoryId)
                .HasDatabaseName("IX_RepositoryClaims_RepositoryId")
                .IsUnique();
            entity.HasIndex(item => item.RepositoryAliasId)
                .HasDatabaseName("IX_RepositoryClaims_RepositoryAliasId")
                .IsUnique();
        });

        builder.Entity<GitCandyRenameEvent>(entity =>
        {
            entity.ToTable("RenameEvents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.EventType).IsRequired();
            entity.Property(item => item.SubjectType).IsRequired();
            entity.Property(item => item.ActorUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.OldSlug).IsRequired().HasMaxLength(SchemaLimits.NamespaceSlug);
            entity.Property(item => item.NewSlug).IsRequired().HasMaxLength(SchemaLimits.NamespaceSlug);
            entity.Property(item => item.OccurredAtUtc).IsRequired();
            entity.Property(item => item.Reason).HasMaxLength(SchemaLimits.RenameReason);
            entity.HasIndex(item => new { item.SubjectType, item.SubjectId, item.EventType, item.OccurredAtUtc })
                .HasDatabaseName("IX_RenameEvents_Subject_Window");
        });

        builder.Entity<GitCandyLegacyRepositoryRoute>(entity =>
        {
            entity.ToTable("LegacyRepositoryRoutes");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Project).IsRequired().HasMaxLength(SchemaLimits.RepositoryName);
            entity.Property(item => item.NormalizedProject).IsRequired().HasMaxLength(SchemaLimits.RepositoryName);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.HasOne(item => item.Repository)
                .WithMany()
                .HasForeignKey(item => item.RepositoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.NormalizedProject)
                .HasDatabaseName("IX_LegacyRepositoryRoutes_NormalizedProject")
                .IsUnique();
            entity.HasIndex(item => item.RepositoryId)
                .HasDatabaseName("IX_LegacyRepositoryRoutes_RepositoryId")
                .IsUnique();
        });
    }
}
