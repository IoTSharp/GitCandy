using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class PullRequestModelConfiguration
{
    public static void ConfigurePullRequestModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyPullRequest>(entity =>
        {
            entity.ToTable("PullRequests");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Title).IsRequired().HasMaxLength(SchemaLimits.IssueTitle);
            entity.Property(item => item.BodyMarkdown).IsRequired().HasMaxLength(SchemaLimits.IssueBody);
            entity.Property(item => item.BodyHtml).IsRequired().HasMaxLength(SchemaLimits.IssueBody);
            entity.Property(item => item.AuthorUserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.SourceBranch).IsRequired().HasMaxLength(SchemaLimits.GitRefName);
            entity.Property(item => item.TargetBranch).IsRequired().HasMaxLength(SchemaLimits.GitRefName);
            entity.Property(item => item.OriginalBaseSha).IsRequired().HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.OriginalHeadSha).IsRequired().HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.CurrentBaseSha).IsRequired().HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.CurrentHeadSha).IsRequired().HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.State).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(item => item.ActivePairKey).IsRequired().HasMaxLength(SchemaLimits.PullRequestPairKey);
            entity.Property(item => item.MergeCommitSha).HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.MergedByUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.Version).IsConcurrencyToken().IsRequired();
            entity.HasOne(item => item.Repository).WithMany(item => item.PullRequests)
                .HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Author).WithMany(item => item.AuthoredPullRequests)
                .HasForeignKey(item => item.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.MergedBy).WithMany()
                .HasForeignKey(item => item.MergedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.RepositoryId, item.Number })
                .HasDatabaseName("IX_PullRequests_RepositoryId_Number").IsUnique();
            entity.HasIndex(item => new { item.RepositoryId, item.ActivePairKey })
                .HasDatabaseName("IX_PullRequests_RepositoryId_ActivePairKey").IsUnique();
            entity.HasIndex(item => new { item.RepositoryId, item.State, item.UpdatedAtUtc })
                .HasDatabaseName("IX_PullRequests_RepositoryId_State_UpdatedAtUtc");
        });

        builder.Entity<GitCandyPullRequestTimelineEvent>(entity =>
        {
            entity.ToTable("PullRequestTimelineEvents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.ActorUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.Detail).HasMaxLength(SchemaLimits.IssueDetail);
            entity.HasOne(item => item.PullRequest).WithMany(item => item.Timeline)
                .HasForeignKey(item => item.PullRequestId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Actor).WithMany()
                .HasForeignKey(item => item.ActorUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(item => new { item.PullRequestId, item.CreatedAtUtc })
                .HasDatabaseName("IX_PullRequestTimelineEvents_PullRequestId_CreatedAtUtc");
        });
    }
}
