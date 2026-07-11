using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class PullRequestReviewModelConfiguration
{
    public static void ConfigurePullRequestReviewModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyPullRequestReviewThread>(entity =>
        {
            entity.ToTable("PullRequestReviewThreads");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.AuthorUserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.OriginalBaseSha).IsRequired().HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.OriginalHeadSha).IsRequired().HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.OriginalPath).IsRequired().HasMaxLength(SchemaLimits.GitPath);
            entity.Property(item => item.OriginalSide).HasConversion<string>().HasMaxLength(8).IsRequired();
            entity.Property(item => item.AnchorContext).IsRequired().HasMaxLength(SchemaLimits.ReviewAnchorContext);
            entity.Property(item => item.CurrentHeadSha).IsRequired().HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.CurrentPath).HasMaxLength(SchemaLimits.GitPath);
            entity.Property(item => item.CurrentSide).HasConversion<string>().HasMaxLength(8);
            entity.Property(item => item.ResolvedByUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.Version).IsConcurrencyToken().IsRequired();
            entity.HasOne(item => item.PullRequest).WithMany(item => item.ReviewThreads).HasForeignKey(item => item.PullRequestId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Author).WithMany().HasForeignKey(item => item.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.ResolvedBy).WithMany().HasForeignKey(item => item.ResolvedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(item => new { item.PullRequestId, item.CreatedAtUtc, item.Id }).HasDatabaseName("IX_PullRequestReviewThreads_PullRequestId_CreatedAtUtc_Id");
            entity.HasIndex(item => new { item.PullRequestId, item.IsOutdated, item.IsResolved }).HasDatabaseName("IX_PullRequestReviewThreads_PullRequestId_Status");
        });

        builder.Entity<GitCandyPullRequestReviewComment>(entity =>
        {
            entity.ToTable("PullRequestReviewComments");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.AuthorUserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.BodyMarkdown).IsRequired().HasMaxLength(SchemaLimits.IssueBody);
            entity.Property(item => item.BodyHtml).IsRequired().HasMaxLength(SchemaLimits.IssueBody * 2);
            entity.Property(item => item.Version).IsConcurrencyToken().IsRequired();
            entity.HasOne(item => item.Thread).WithMany(item => item.Comments).HasForeignKey(item => item.ThreadId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Author).WithMany().HasForeignKey(item => item.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.ThreadId, item.CreatedAtUtc, item.Id }).HasDatabaseName("IX_PullRequestReviewComments_ThreadId_CreatedAtUtc_Id");
        });
    }
}
