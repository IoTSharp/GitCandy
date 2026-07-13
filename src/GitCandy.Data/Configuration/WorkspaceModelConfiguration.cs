using GitCandy.Data.Domain;
using GitCandy.Data.Identity;
using GitCandy.Notifications;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class WorkspaceModelConfiguration
{
    public static void ConfigureWorkspaceModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyTodo>(entity =>
        {
            entity.ToTable("Todos");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.UserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.ResourceType).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.ResourceId).IsRequired().HasMaxLength(128);
            entity.Property(item => item.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(item => item.Title).IsRequired().HasMaxLength(300);
            entity.Property(item => item.Url).IsRequired().HasMaxLength(600);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasOne<GitCandyUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GitCandyRepository>().WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GitCandyTeam>().WithMany().HasForeignKey(item => item.TeamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.UserId, item.Kind, item.ResourceType, item.ResourceId })
                .HasDatabaseName("IX_Todos_User_Kind_Resource").IsUnique();
            entity.HasIndex(item => new { item.UserId, item.Status, item.SnoozedUntilUtc, item.UpdatedAtUtc })
                .HasDatabaseName("IX_Todos_User_Status_Snooze_Updated");
        });

        builder.Entity<GitCandyNotification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.EventId).IsRequired().HasMaxLength(128);
            entity.Property(item => item.UserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.ActorUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.EventType).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.ResourceType).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.ResourceId).IsRequired().HasMaxLength(128);
            entity.Property(item => item.Reason).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.Title).IsRequired().HasMaxLength(300);
            entity.Property(item => item.Url).IsRequired().HasMaxLength(600);
            entity.HasOne<GitCandyUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GitCandyRepository>().WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GitCandyTeam>().WithMany().HasForeignKey(item => item.TeamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.UserId, item.EventId })
                .HasDatabaseName("IX_Notifications_User_Event").IsUnique();
            entity.HasIndex(item => new { item.UserId, item.ReadAtUtc, item.CreatedAtUtc })
                .HasDatabaseName("IX_Notifications_User_Read_Created");
        });

        builder.Entity<GitCandyNotificationPreference>(entity =>
        {
            entity.ToTable("NotificationPreferences");
            entity.HasKey(item => new { item.UserId, item.EventType });
            entity.Property(item => item.UserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.EventType).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.WebhookUrl).HasMaxLength(SchemaLimits.TargetUrl);
            entity.Property(item => item.ProtectedWebhookSecret).HasMaxLength(SchemaLimits.ProtectedSecret);
            entity.HasOne<GitCandyUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GitCandyNotificationDelivery>(entity =>
        {
            entity.ToTable("NotificationDeliveries");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(SchemaLimits.WebhookDeliveryId);
            entity.Property(item => item.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(item => item.State).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(item => item.Recipient).IsRequired().HasMaxLength(SchemaLimits.TargetUrl);
            entity.Property(item => item.ProtectedSecret).HasMaxLength(SchemaLimits.ProtectedSecret);
            entity.Property(item => item.ErrorCode).HasMaxLength(SchemaLimits.WebhookErrorCode);
            entity.HasOne(item => item.Notification).WithMany(item => item.Deliveries)
                .HasForeignKey(item => item.NotificationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.NotificationId, item.Channel })
                .HasDatabaseName("IX_NotificationDeliveries_Notification_Channel").IsUnique();
            entity.HasIndex(item => new { item.State, item.NextAttemptAtUtc })
                .HasDatabaseName("IX_NotificationDeliveries_State_NextAttempt");
        });

        builder.Entity<GitCandyActivityEvent>(entity =>
        {
            entity.ToTable("ActivityEvents");
            entity.HasKey(item => item.EventId);
            entity.Property(item => item.EventId).HasMaxLength(128);
            entity.Property(item => item.ActorUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.ResourceType).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.ResourceId).IsRequired().HasMaxLength(128);
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.Title).IsRequired().HasMaxLength(300);
            entity.Property(item => item.Url).IsRequired().HasMaxLength(600);
            entity.HasOne<GitCandyRepository>().WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GitCandyTeam>().WithMany().HasForeignKey(item => item.TeamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.RepositoryId, item.OccurredAtUtc, item.EventId })
                .HasDatabaseName("IX_ActivityEvents_Repository_Occurred_Event");
            entity.HasIndex(item => item.RetainUntilUtc).HasDatabaseName("IX_ActivityEvents_RetainUntilUtc");
        });

        builder.Entity<GitCandyRepositoryStar>(entity =>
        {
            entity.ToTable("RepositoryStars");
            entity.HasKey(item => new { item.UserId, item.RepositoryId });
            entity.Property(item => item.UserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.HasOne<GitCandyUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GitCandyRepository>().WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.RepositoryId, item.CreatedAtUtc })
                .HasDatabaseName("IX_RepositoryStars_Repository_Created");
        });

        builder.Entity<GitCandyRepositoryInteraction>(entity =>
        {
            entity.ToTable("RepositoryInteractions");
            entity.HasKey(item => new { item.UserId, item.RepositoryId });
            entity.Property(item => item.UserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.HasOne<GitCandyUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<GitCandyRepository>().WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.UserId, item.LastInteractedAtUtc })
                .HasDatabaseName("IX_RepositoryInteractions_User_Last");
        });

        builder.Entity<GitCandyRepositoryMetricDaily>(entity =>
        {
            entity.ToTable("RepositoryMetricsDaily");
            entity.HasKey(item => new { item.RepositoryId, item.DayUtc });
            entity.Property(item => item.LicenseSpdx).HasMaxLength(64);
            entity.HasOne<GitCandyRepository>().WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => item.DayUtc).HasDatabaseName("IX_RepositoryMetricsDaily_DayUtc");
        });

        builder.Entity<GitCandyRepositoryPageView>(entity =>
        {
            entity.ToTable("RepositoryPageViews");
            entity.HasKey(item => new { item.RepositoryId, item.DayUtc, item.VisitorKey });
            entity.Property(item => item.VisitorKey).HasMaxLength(64);
            entity.HasOne<GitCandyRepository>().WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => item.DayUtc).HasDatabaseName("IX_RepositoryPageViews_DayUtc");
        });

        builder.Entity<GitCandyRepositoryRecommendationSnapshot>(entity =>
        {
            entity.ToTable("RepositoryRecommendationSnapshots");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.SnapshotId).IsRequired().HasMaxLength(32);
            entity.Property(item => item.AlgorithmVersion).IsRequired().HasMaxLength(32);
            entity.Property(item => item.Explanation).IsRequired().HasMaxLength(200);
            entity.HasOne<GitCandyRepository>().WithMany().HasForeignKey(item => item.RepositoryId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.SnapshotId, item.RepositoryId })
                .HasDatabaseName("IX_RecommendationSnapshots_Snapshot_Repository").IsUnique();
            entity.HasIndex(item => new { item.CalculatedAtUtc, item.Rank })
                .HasDatabaseName("IX_RecommendationSnapshots_Calculated_Rank");
        });
    }
}
