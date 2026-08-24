using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class RemoteModelConfiguration
{
    public static void ConfigureRemoteModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyRemoteAccountConnection>(entity =>
        {
            entity.ToTable("RemoteAccountConnections", table => table.HasCheckConstraint(
                "CK_RemoteAccountConnections_Owner",
                "(OwnerKind = 'User' AND OwnerUserId IS NOT NULL AND OwnerTeamId IS NULL) OR "
                + "(OwnerKind = 'Team' AND OwnerUserId IS NULL AND OwnerTeamId IS NOT NULL)"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.OwnerKind).HasConversion<string>().IsRequired().HasMaxLength(16);
            entity.Property(item => item.OwnerUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.Provider).HasConversion<string>().IsRequired().HasMaxLength(16);
            entity.Property(item => item.ServerUrl).IsRequired().HasMaxLength(SchemaLimits.RemoteServerUrl);
            entity.Property(item => item.ExternalAccountId).IsRequired().HasMaxLength(SchemaLimits.RemoteExternalId);
            entity.Property(item => item.AccountKind).HasConversion<string>().IsRequired().HasMaxLength(24);
            entity.Property(item => item.Login).IsRequired().HasMaxLength(SchemaLimits.RemoteLogin);
            entity.Property(item => item.DisplayName).HasMaxLength(SchemaLimits.UserDisplayName);
            entity.Property(item => item.AuthenticationKind).HasConversion<string>().IsRequired().HasMaxLength(32);
            entity.Property(item => item.CredentialReference).IsRequired().HasMaxLength(SchemaLimits.SecretReference);
            entity.Property(item => item.WebhookSecretReference).HasMaxLength(SchemaLimits.SecretReference);
            entity.Property(item => item.GrantedScopes).IsRequired().HasMaxLength(SchemaLimits.RemoteGrantedScopes);
            entity.Property(item => item.IsEnabled).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().IsRequired().HasMaxLength(24);
            entity.Property(item => item.LastErrorCode).HasMaxLength(SchemaLimits.WebhookErrorCode);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.UpdatedAtUtc).IsRequired();
            entity.HasOne(item => item.OwnerUser)
                .WithMany()
                .HasForeignKey(item => item.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.OwnerTeam)
                .WithMany()
                .HasForeignKey(item => item.OwnerTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.Provider, item.ServerUrl, item.ExternalAccountId })
                .HasDatabaseName("IX_RemoteAccountConnections_StableIdentity")
                .IsUnique();
            entity.HasIndex(item => new { item.OwnerUserId, item.IsEnabled })
                .HasDatabaseName("IX_RemoteAccountConnections_User_Enabled");
            entity.HasIndex(item => new { item.OwnerTeamId, item.IsEnabled })
                .HasDatabaseName("IX_RemoteAccountConnections_Team_Enabled");
        });

        builder.Entity<GitCandyRepositoryMirror>(entity =>
        {
            entity.ToTable("RepositoryMirrors", table =>
            {
                table.HasCheckConstraint(
                    "CK_RepositoryMirrors_DirectionAuthority",
                    "(Direction = 'Pull' AND Authority = 'Remote') OR "
                    + "(Direction = 'Push' AND Authority = 'GitCandy')");
                table.HasCheckConstraint(
                    "CK_RepositoryMirrors_ScheduleInterval",
                    "ScheduleIntervalMinutes IS NULL OR "
                    + "(ScheduleIntervalMinutes >= 5 AND ScheduleIntervalMinutes <= 10080)");
                table.HasCheckConstraint(
                    "CK_RepositoryMirrors_ScheduleConfiguration",
                    "(ScheduleIntervalMinutes IS NULL AND ScheduleTimeZone IS NULL) OR "
                    + "(ScheduleIntervalMinutes IS NOT NULL AND ScheduleTimeZone IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_RepositoryMirrors_RefFilter",
                    "(RefFilterKind IN ('AllRefs', 'ProtectedBranches') AND RefFilterPattern IS NULL) OR "
                    + "(RefFilterKind IN ('AllowList', 'RegularExpression') AND RefFilterPattern IS NOT NULL)");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.RemoteRepositoryId).IsRequired().HasMaxLength(SchemaLimits.RemoteExternalId);
            entity.Property(item => item.RemoteOwnerLogin).IsRequired().HasMaxLength(SchemaLimits.RemoteLogin);
            entity.Property(item => item.RemoteRepositoryName).IsRequired().HasMaxLength(SchemaLimits.RemoteLogin);
            entity.Property(item => item.RemoteGitUrl).IsRequired().HasMaxLength(SchemaLimits.TargetUrl);
            entity.Property(item => item.Direction).HasConversion<string>().IsRequired().HasMaxLength(8);
            entity.Property(item => item.Authority).HasConversion<string>().IsRequired().HasMaxLength(16);
            entity.Property(item => item.RefFilterKind).HasConversion<string>().IsRequired().HasMaxLength(32);
            entity.Property(item => item.RefFilterPattern).HasMaxLength(SchemaLimits.RemoteRefFilter);
            entity.Property(item => item.ScheduleTimeZone).HasMaxLength(SchemaLimits.TimeZoneId);
            entity.Property(item => item.ScheduleEnabled).IsRequired();
            entity.Property(item => item.DivergencePolicy).HasConversion<string>().IsRequired().HasMaxLength(24);
            entity.Property(item => item.Prune).IsRequired();
            entity.Property(item => item.IsEnabled).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().IsRequired().HasMaxLength(24);
            entity.Property(item => item.LastObservedRemoteHead).HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.LastErrorCode).HasMaxLength(SchemaLimits.WebhookErrorCode);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.UpdatedAtUtc).IsRequired();
            entity.HasOne(item => item.Repository)
                .WithMany()
                .HasForeignKey(item => item.RepositoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Connection)
                .WithMany(connection => connection.Mirrors)
                .HasForeignKey(item => item.ConnectionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new
                {
                    item.RepositoryId,
                    item.ConnectionId,
                    item.RemoteRepositoryId
                })
                .HasDatabaseName("IX_RepositoryMirrors_Target_Direction")
                .IsUnique();
            entity.HasIndex(item => new { item.ScheduleEnabled, item.IsEnabled, item.Status })
                .HasDatabaseName("IX_RepositoryMirrors_Schedule_Status");
        });

        builder.Entity<GitCandyRemoteMirrorRefUpdate>(entity =>
        {
            entity.ToTable("RemoteMirrorRefUpdates");
            entity.HasKey(item => new { item.MirrorId, item.ReferenceName });
            entity.Property(item => item.ReferenceName).HasMaxLength(SchemaLimits.GitRefName);
            entity.Property(item => item.OldObjectId).IsRequired().HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.NewObjectId).IsRequired().HasMaxLength(SchemaLimits.CommitSha);
            entity.Property(item => item.Generation).IsRequired();
            entity.Property(item => item.EnqueuedAtUtc).IsRequired();
            entity.Property(item => item.UpdatedAtUtc).IsRequired();
            entity.HasOne(item => item.Mirror)
                .WithMany(mirror => mirror.PendingRefUpdates)
                .HasForeignKey(item => item.MirrorId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => item.UpdatedAtUtc)
                .HasDatabaseName("IX_RemoteMirrorRefUpdates_UpdatedAtUtc");
        });

        builder.Entity<GitCandyRemoteMirrorJob>(entity =>
        {
            entity.ToTable("RemoteMirrorJobs", table =>
            {
                table.HasCheckConstraint(
                    "CK_RemoteMirrorJobs_Generation",
                    "RequestedGeneration >= 1 AND ProcessedGeneration >= 0 "
                    + "AND ProcessedGeneration <= RequestedGeneration");
                table.HasCheckConstraint(
                    "CK_RemoteMirrorJobs_AttemptCount",
                    "AttemptCount >= 0");
                table.HasCheckConstraint(
                    "CK_RemoteMirrorJobs_Lease",
                    "(State = 'Leased' AND LeaseOwner IS NOT NULL AND LeaseExpiresAtUtc IS NOT NULL) OR "
                    + "(State <> 'Leased' AND LeaseOwner IS NULL AND LeaseExpiresAtUtc IS NULL)");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.State).HasConversion<string>().IsRequired().HasMaxLength(16);
            entity.Property(item => item.Triggers).HasConversion<int>().IsRequired();
            entity.Property(item => item.RequestedGeneration).IsRequired();
            entity.Property(item => item.ProcessedGeneration).IsRequired();
            entity.Property(item => item.AttemptCount).IsRequired();
            entity.Property(item => item.AvailableAtUtc).IsRequired();
            entity.Property(item => item.LeaseOwner).HasMaxLength(SchemaLimits.RemoteLeaseOwner);
            entity.Property(item => item.LastErrorCode).HasMaxLength(SchemaLimits.WebhookErrorCode);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.UpdatedAtUtc).IsRequired();
            entity.Property(item => item.Version).IsConcurrencyToken().IsRequired();
            entity.HasOne(item => item.Mirror)
                .WithOne(mirror => mirror.Job)
                .HasForeignKey<GitCandyRemoteMirrorJob>(item => item.MirrorId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => item.MirrorId)
                .HasDatabaseName("IX_RemoteMirrorJobs_MirrorId")
                .IsUnique();
            entity.HasIndex(item => new { item.State, item.AvailableAtUtc })
                .HasDatabaseName("IX_RemoteMirrorJobs_State_AvailableAtUtc");
            entity.HasIndex(item => item.LeaseExpiresAtUtc)
                .HasDatabaseName("IX_RemoteMirrorJobs_LeaseExpiresAtUtc");
        });

        builder.Entity<GitCandyRemoteProviderEvent>(entity =>
        {
            entity.ToTable("RemoteProviderEvents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Provider).HasConversion<string>().IsRequired().HasMaxLength(16);
            entity.Property(item => item.DeliveryId).IsRequired().HasMaxLength(SchemaLimits.RemoteDeliveryId);
            entity.Property(item => item.EventType).IsRequired().HasMaxLength(SchemaLimits.RemoteEventType);
            entity.Property(item => item.PayloadHash).IsRequired().HasMaxLength(SchemaLimits.Sha256Hash);
            entity.Property(item => item.ReceivedAtUtc).IsRequired();
            entity.HasOne(item => item.Connection)
                .WithMany(connection => connection.ProviderEvents)
                .HasForeignKey(item => item.ConnectionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.ConnectionId, item.DeliveryId })
                .HasDatabaseName("IX_RemoteProviderEvents_Connection_Delivery")
                .IsUnique();
            entity.HasIndex(item => item.ReceivedAtUtc)
                .HasDatabaseName("IX_RemoteProviderEvents_ReceivedAtUtc");
        });
    }
}
