using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class IntegrationModelConfiguration
{
    public static void ConfigureIntegrationModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyIntegrationEvent>(entity =>
        {
            entity.ToTable("IntegrationEvents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(SchemaLimits.IntegrationEventId);
            entity.Property(item => item.SchemaVersion).IsRequired();
            entity.Property(item => item.Type).IsRequired().HasMaxLength(SchemaLimits.IntegrationEventType);
            entity.Property(item => item.ActorUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.ActorName).IsRequired().HasMaxLength(SchemaLimits.IntegrationActorName);
            entity.Property(item => item.PayloadJson).IsRequired().HasMaxLength(SchemaLimits.IntegrationPayload);
            entity.Property(item => item.OccurredAtUtc).IsRequired();
            entity.HasOne(item => item.Repository)
                .WithMany(repository => repository.IntegrationEvents)
                .HasForeignKey(item => item.RepositoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.RepositoryId, item.OccurredAtUtc })
                .HasDatabaseName("IX_IntegrationEvents_RepositoryId_OccurredAtUtc");
        });

        builder.Entity<GitCandyWebhookSubscription>(entity =>
        {
            entity.ToTable("WebhookSubscriptions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Name).IsRequired().HasMaxLength(SchemaLimits.IntegrationName);
            entity.Property(item => item.NormalizedName).IsRequired().HasMaxLength(SchemaLimits.IntegrationName);
            entity.Property(item => item.TargetUrl).IsRequired().HasMaxLength(SchemaLimits.TargetUrl);
            entity.Property(item => item.ProtectedSecret).IsRequired().HasMaxLength(SchemaLimits.ProtectedSecret);
            entity.Property(item => item.Events).HasConversion<int>().IsRequired();
            entity.Property(item => item.IsActive).IsRequired();
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.UpdatedAtUtc).IsRequired();
            entity.HasOne(item => item.Repository)
                .WithMany(repository => repository.WebhookSubscriptions)
                .HasForeignKey(item => item.RepositoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.RepositoryId, item.NormalizedName })
                .HasDatabaseName("IX_WebhookSubscriptions_RepositoryId_Name")
                .IsUnique();
        });

        builder.Entity<GitCandyWebhookDelivery>(entity =>
        {
            entity.ToTable("WebhookDeliveries");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(SchemaLimits.WebhookDeliveryId);
            entity.Property(item => item.EventId).IsRequired().HasMaxLength(SchemaLimits.IntegrationEventId);
            entity.Property(item => item.State).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.AttemptCount).IsRequired();
            entity.Property(item => item.ErrorCode).HasMaxLength(SchemaLimits.WebhookErrorCode);
            entity.Property(item => item.ReplayOfDeliveryId).HasMaxLength(SchemaLimits.WebhookDeliveryId);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.HasOne(item => item.Subscription)
                .WithMany(subscription => subscription.Deliveries)
                .HasForeignKey(item => item.SubscriptionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Event)
                .WithMany(integrationEvent => integrationEvent.Deliveries)
                .HasForeignKey(item => item.EventId)
                .IsRequired()
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(item => new { item.State, item.NextAttemptAtUtc, item.LeaseExpiresAtUtc })
                .HasDatabaseName("IX_WebhookDeliveries_State_NextAttempt_Lease");
            entity.HasIndex(item => new { item.SubscriptionId, item.EventId })
                .HasDatabaseName("IX_WebhookDeliveries_SubscriptionId_EventId");
        });

        builder.Entity<GitCandyCommitCheck>(entity =>
        {
            entity.ToTable("CommitChecks");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Sha).IsRequired().HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(item => item.Context).IsRequired().HasMaxLength(SchemaLimits.CheckContext);
            entity.Property(item => item.State).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(item => item.Description).IsRequired().HasMaxLength(SchemaLimits.CheckDescription);
            entity.Property(item => item.TargetUrl).HasMaxLength(SchemaLimits.TargetUrl);
            entity.Property(item => item.ExternalId).HasMaxLength(SchemaLimits.CheckExternalId);
            entity.Property(item => item.ActorUserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.UpdatedAtUtc).IsRequired();
            entity.HasOne(item => item.Repository)
                .WithMany(repository => repository.CommitChecks)
                .HasForeignKey(item => item.RepositoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.RepositoryId, item.Sha, item.Kind, item.Context })
                .HasDatabaseName("IX_CommitChecks_Repository_Sha_Kind_Context")
                .IsUnique();
            entity.HasIndex(item => new { item.RepositoryId, item.Sha, item.State })
                .HasDatabaseName("IX_CommitChecks_Repository_Sha_State");
        });
    }
}
