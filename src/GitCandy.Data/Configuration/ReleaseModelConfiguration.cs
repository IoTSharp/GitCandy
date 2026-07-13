using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class ReleaseModelConfiguration
{
    public static void ConfigureReleaseModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyRelease>(entity =>
        {
            entity.ToTable("Releases");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.TagName).IsRequired().HasMaxLength(SchemaLimits.GitRefName);
            entity.Property(item => item.NormalizedTagName).IsRequired().HasMaxLength(SchemaLimits.GitRefName);
            entity.Property(item => item.TagCommitSha).IsRequired().HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.Name).IsRequired().HasMaxLength(SchemaLimits.ReleaseName);
            entity.Property(item => item.BodyMarkdown).IsRequired().HasMaxLength(SchemaLimits.IssueBody);
            entity.Property(item => item.BodyHtml).IsRequired().HasMaxLength(SchemaLimits.IssueBody);
            entity.Property(item => item.CreatedByUserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.HasOne(item => item.Repository).WithMany().HasForeignKey(item => item.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.RepositoryId, item.NormalizedTagName })
                .HasDatabaseName("IX_Releases_Repository_Tag").IsUnique();
            entity.HasIndex(item => new { item.RepositoryId, item.PublishedAtUtc })
                .HasDatabaseName("IX_Releases_Repository_Published");
        });
        builder.Entity<GitCandyReleaseAsset>(entity =>
        {
            entity.ToTable("ReleaseAssets");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(32);
            entity.Property(item => item.FileName).IsRequired().HasMaxLength(SchemaLimits.ReleaseAssetName);
            entity.Property(item => item.ContentType).IsRequired().HasMaxLength(SchemaLimits.ReleaseContentType);
            entity.Property(item => item.Sha256).IsRequired().IsFixedLength().HasMaxLength(SchemaLimits.Sha256Hash);
            entity.HasOne(item => item.Release).WithMany(item => item.Assets).HasForeignKey(item => item.ReleaseId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.ReleaseId, item.FileName })
                .HasDatabaseName("IX_ReleaseAssets_Release_FileName").IsUnique();
        });
    }
}
