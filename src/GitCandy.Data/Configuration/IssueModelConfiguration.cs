using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class IssueModelConfiguration
{
    public static void ConfigureIssueModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyWorkItemSequence>(entity =>
        {
            entity.ToTable("WorkItemSequences");
            entity.HasKey(item => item.RepositoryId);
            entity.Property(item => item.NextNumber).IsRequired();
            entity.Property(item => item.Version).IsConcurrencyToken().IsRequired();
            entity.HasOne(item => item.Repository).WithOne(item => item.WorkItemSequence)
                .HasForeignKey<GitCandyWorkItemSequence>(item => item.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GitCandyIssue>(entity =>
        {
            entity.ToTable("Issues");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Title).IsRequired().HasMaxLength(SchemaLimits.IssueTitle);
            entity.Property(item => item.BodyMarkdown).IsRequired().HasMaxLength(SchemaLimits.IssueBody);
            entity.Property(item => item.BodyHtml).IsRequired().HasMaxLength(SchemaLimits.IssueBody * 2);
            entity.Property(item => item.AuthorUserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.AssigneeUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.State).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(item => item.Version).IsConcurrencyToken().IsRequired();
            entity.HasOne(item => item.Repository).WithMany(item => item.Issues)
                .HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Author).WithMany(item => item.AuthoredIssues)
                .HasForeignKey(item => item.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Assignee).WithMany()
                .HasForeignKey(item => item.AssigneeUserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.Milestone).WithMany(item => item.Issues)
                .HasForeignKey(item => item.MilestoneId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(item => new { item.RepositoryId, item.Number })
                .HasDatabaseName("IX_Issues_RepositoryId_Number").IsUnique();
            entity.HasIndex(item => new { item.RepositoryId, item.State, item.UpdatedAtUtc })
                .HasDatabaseName("IX_Issues_RepositoryId_State_UpdatedAtUtc");
            entity.HasIndex(item => item.AuthorUserId).HasDatabaseName("IX_Issues_AuthorUserId");
            entity.HasIndex(item => item.AssigneeUserId).HasDatabaseName("IX_Issues_AssigneeUserId");
        });

        builder.Entity<GitCandyIssueComment>(entity =>
        {
            entity.ToTable("IssueComments");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.AuthorUserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.HiddenByUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.BodyMarkdown).IsRequired().HasMaxLength(SchemaLimits.IssueBody);
            entity.Property(item => item.BodyHtml).IsRequired().HasMaxLength(SchemaLimits.IssueBody * 2);
            entity.Property(item => item.Version).IsConcurrencyToken().IsRequired();
            entity.HasOne(item => item.Issue).WithMany(item => item.Comments)
                .HasForeignKey(item => item.IssueId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Author).WithMany()
                .HasForeignKey(item => item.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.IssueId, item.CreatedAtUtc })
                .HasDatabaseName("IX_IssueComments_IssueId_CreatedAtUtc");
        });

        builder.Entity<GitCandyIssueEdit>(entity =>
        {
            entity.ToTable("IssueEdits");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.EditorUserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.PreviousMarkdown).IsRequired().HasMaxLength(SchemaLimits.IssueBody);
            entity.Property(item => item.PreviousHtml).IsRequired().HasMaxLength(SchemaLimits.IssueBody * 2);
            entity.HasOne<GitCandyIssue>().WithMany().HasForeignKey(item => item.IssueId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GitCandyIssueComment>().WithMany().HasForeignKey(item => item.CommentId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(item => new { item.IssueId, item.EditedAtUtc }).HasDatabaseName("IX_IssueEdits_IssueId_EditedAtUtc");
        });

        builder.Entity<GitCandyIssueTimelineEvent>(entity =>
        {
            entity.ToTable("IssueTimelineEvents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.ActorUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.Detail).HasMaxLength(SchemaLimits.IssueDetail);
            entity.HasOne(item => item.Issue).WithMany(item => item.Timeline)
                .HasForeignKey(item => item.IssueId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Comment).WithMany().HasForeignKey(item => item.CommentId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(item => item.Actor).WithMany().HasForeignKey(item => item.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.IssueId, item.CreatedAtUtc, item.Id })
                .HasDatabaseName("IX_IssueTimelineEvents_IssueId_CreatedAtUtc_Id");
            entity.HasIndex(item => new { item.ActorUserId, item.Type, item.CreatedAtUtc })
                .HasDatabaseName("IX_IssueTimelineEvents_ActorUserId_Type_CreatedAtUtc");
        });

        builder.Entity<GitCandyIssueLabel>(entity =>
        {
            entity.ToTable("IssueLabels");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Name).IsRequired().HasMaxLength(SchemaLimits.IssueLabelName);
            entity.Property(item => item.NormalizedName).IsRequired().HasMaxLength(SchemaLimits.IssueLabelName);
            entity.Property(item => item.Color).IsRequired().IsFixedLength().HasMaxLength(SchemaLimits.IssueLabelColor);
            entity.Property(item => item.Description).IsRequired().HasMaxLength(SchemaLimits.IssueLabelDescription);
            entity.HasOne(item => item.Repository).WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.RepositoryId, item.NormalizedName })
                .HasDatabaseName("IX_IssueLabels_RepositoryId_NormalizedName").IsUnique();
        });

        builder.Entity<GitCandyIssueLabelLink>(entity =>
        {
            entity.ToTable("IssueLabelLinks");
            entity.HasKey(item => new { item.IssueId, item.LabelId });
            entity.HasOne(item => item.Issue).WithMany(item => item.LabelLinks).HasForeignKey(item => item.IssueId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Label).WithMany(item => item.IssueLinks).HasForeignKey(item => item.LabelId).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<GitCandyIssueMilestone>(entity =>
        {
            entity.ToTable("IssueMilestones");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Title).IsRequired().HasMaxLength(SchemaLimits.IssueMilestoneTitle);
            entity.Property(item => item.Description).IsRequired().HasMaxLength(SchemaLimits.IssueMilestoneDescription);
            entity.HasOne(item => item.Repository).WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.RepositoryId, item.Title }).HasDatabaseName("IX_IssueMilestones_RepositoryId_Title");
        });

        builder.Entity<GitCandyIssueSubscription>(entity =>
        {
            entity.ToTable("IssueSubscriptions");
            entity.HasKey(item => new { item.IssueId, item.UserId });
            entity.Property(item => item.UserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.HasOne(item => item.Issue).WithMany(item => item.Subscriptions).HasForeignKey(item => item.IssueId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GitCandyIssueNotification>(entity =>
        {
            entity.ToTable("IssueNotifications");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.UserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.ActorUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.HasOne<GitCandyRepository>().WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<GitCandyIssue>().WithMany().HasForeignKey(item => item.IssueId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GitCandyUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.UserId, item.ReadAtUtc, item.CreatedAtUtc })
                .HasDatabaseName("IX_IssueNotifications_UserId_ReadAtUtc_CreatedAtUtc");
        });

        builder.Entity<GitCandyIssueRelation>(entity =>
        {
            entity.ToTable("IssueRelations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(item => item.CreatedByUserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.HasOne<GitCandyRepository>().WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GitCandyIssue>().WithMany().HasForeignKey(item => item.SourceIssueId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<GitCandyIssue>().WithMany().HasForeignKey(item => item.TargetIssueId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(item => new { item.SourceIssueId, item.TargetIssueId, item.Type })
                .HasDatabaseName("IX_IssueRelations_Source_Target_Type").IsUnique();
        });

        builder.Entity<GitCandyIssueReference>(entity =>
        {
            entity.ToTable("IssueReferences");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.CommitSha).HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.DisplayText).IsRequired().HasMaxLength(200);
            entity.HasOne<GitCandyIssue>().WithMany().HasForeignKey(item => item.SourceIssueId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GitCandyRepository>().WithMany().HasForeignKey(item => item.TargetRepositoryId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<GitCandyIssue>().WithMany().HasForeignKey(item => item.TargetIssueId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(item => item.SourceIssueId).HasDatabaseName("IX_IssueReferences_SourceIssueId");
        });
    }
}
