using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class EnterpriseModelConfiguration
{
    public static void ConfigureEnterpriseModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyEnterpriseConnection>(entity =>
        {
            entity.ToTable("EnterpriseConnections");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.Name).IsRequired().HasMaxLength(SchemaLimits.EnterpriseConnectionName);
            entity.Property(item => item.NormalizedName).IsRequired().HasMaxLength(SchemaLimits.EnterpriseConnectionName);
            entity.Property(item => item.Provider).HasConversion<string>().IsRequired().HasMaxLength(32);
            entity.Property(item => item.ExternalOrganizationId).IsRequired().HasMaxLength(SchemaLimits.EnterpriseExternalId);
            entity.Property(item => item.Authority).HasMaxLength(SchemaLimits.TargetUrl);
            entity.Property(item => item.ClientId).HasMaxLength(SchemaLimits.EnterpriseClientId);
            entity.Property(item => item.ApiBaseUrl).HasMaxLength(SchemaLimits.TargetUrl);
            entity.Property(item => item.ConfigurationJson).HasMaxLength(SchemaLimits.EnterpriseConfiguration);
            entity.Property(item => item.SecretReference).IsRequired().HasMaxLength(SchemaLimits.SecretReference);
            entity.Property(item => item.WebhookSecretReference).HasMaxLength(SchemaLimits.SecretReference);
            entity.Property(item => item.SyncCursor).HasMaxLength(SchemaLimits.EnterpriseSyncCursor);
            entity.Property(item => item.LoginEnabled).IsRequired();
            entity.Property(item => item.ProvisioningEnabled).IsRequired();
            entity.Property(item => item.IsEnabled).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().IsRequired().HasMaxLength(24);
            entity.Property(item => item.LastErrorCode).HasMaxLength(SchemaLimits.WebhookErrorCode);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.UpdatedAtUtc).IsRequired();
            entity.HasOne(item => item.Team)
                .WithMany()
                .HasForeignKey(item => item.TeamId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.TeamId, item.NormalizedName })
                .HasDatabaseName("IX_EnterpriseConnections_TeamId_Name")
                .IsUnique();
            entity.HasIndex(item => new { item.TeamId, item.Provider, item.ExternalOrganizationId })
                .HasDatabaseName("IX_EnterpriseConnections_Team_Provider_Organization")
                .IsUnique();
        });

        builder.Entity<GitCandyEnterpriseExternalIdentity>(entity =>
        {
            entity.ToTable("EnterpriseExternalIdentities");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.ExternalId).IsRequired().HasMaxLength(SchemaLimits.EnterpriseExternalId);
            entity.Property(item => item.UserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.UserName).IsRequired().HasMaxLength(SchemaLimits.IdentityStoreKey);
            entity.Property(item => item.NormalizedUserName).IsRequired().HasMaxLength(SchemaLimits.IdentityStoreKey);
            entity.Property(item => item.Email).HasMaxLength(SchemaLimits.IdentityStoreKey);
            entity.Property(item => item.DisplayName).HasMaxLength(SchemaLimits.UserDisplayName);
            entity.Property(item => item.IsActive).IsRequired();
            entity.Property(item => item.FirstSeenAtUtc).IsRequired();
            entity.Property(item => item.LastSeenAtUtc).IsRequired();
            entity.HasOne(item => item.Connection)
                .WithMany(connection => connection.ExternalIdentities)
                .HasForeignKey(item => item.ConnectionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.ConnectionId, item.ExternalId })
                .HasDatabaseName("IX_EnterpriseExternalIdentities_ConnectionId_ExternalId")
                .IsUnique();
            entity.HasIndex(item => new { item.ConnectionId, item.NormalizedUserName })
                .HasDatabaseName("IX_EnterpriseExternalIdentities_ConnectionId_UserName");
            entity.HasIndex(item => item.UserId)
                .HasDatabaseName("IX_EnterpriseExternalIdentities_UserId");
        });

        builder.Entity<GitCandyEnterpriseScimCredential>(entity =>
        {
            entity.ToTable("EnterpriseScimCredentials");
            entity.HasKey(item => item.ConnectionId);
            entity.Property(item => item.Prefix).IsRequired().HasMaxLength(SchemaLimits.TokenPrefix);
            entity.Property(item => item.TokenHash).IsRequired().HasMaxLength(SchemaLimits.Sha256Hash);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.HasOne(item => item.Connection)
                .WithOne(connection => connection.ScimCredential)
                .HasForeignKey<GitCandyEnterpriseScimCredential>(item => item.ConnectionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => item.Prefix)
                .HasDatabaseName("IX_EnterpriseScimCredentials_Prefix")
                .IsUnique();
        });

        builder.Entity<GitCandyEnterpriseGroup>(entity =>
        {
            entity.ToTable("EnterpriseGroups");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.ExternalId).IsRequired().HasMaxLength(SchemaLimits.EnterpriseExternalId);
            entity.Property(item => item.DisplayName).IsRequired().HasMaxLength(SchemaLimits.EnterpriseGroupName);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.UpdatedAtUtc).IsRequired();
            entity.HasOne(item => item.Connection)
                .WithMany(connection => connection.Groups)
                .HasForeignKey(item => item.ConnectionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.ConnectionId, item.ExternalId })
                .HasDatabaseName("IX_EnterpriseGroups_ConnectionId_ExternalId")
                .IsUnique();
            entity.HasIndex(item => new { item.ConnectionId, item.DisplayName })
                .HasDatabaseName("IX_EnterpriseGroups_ConnectionId_DisplayName");
        });

        builder.Entity<GitCandyEnterpriseGroupMember>(entity =>
        {
            entity.ToTable("EnterpriseGroupMembers");
            entity.HasKey(item => new { item.GroupId, item.ExternalIdentityId });
            entity.HasOne(item => item.Group)
                .WithMany(group => group.Members)
                .HasForeignKey(item => item.GroupId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.ExternalIdentity)
                .WithMany()
                .HasForeignKey(item => item.ExternalIdentityId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => item.ExternalIdentityId)
                .HasDatabaseName("IX_EnterpriseGroupMembers_ExternalIdentityId");
        });

        builder.Entity<GitCandyEnterpriseProviderEvent>(entity =>
        {
            entity.ToTable("EnterpriseProviderEvents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.EventId).IsRequired().HasMaxLength(SchemaLimits.EnterpriseEventId);
            entity.Property(item => item.PayloadHash).IsRequired().HasMaxLength(SchemaLimits.Sha256Hash);
            entity.Property(item => item.ReceivedAtUtc).IsRequired();
            entity.HasOne(item => item.Connection)
                .WithMany(connection => connection.ProviderEvents)
                .HasForeignKey(item => item.ConnectionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.ConnectionId, item.EventId })
                .HasDatabaseName("IX_EnterpriseProviderEvents_ConnectionId_EventId")
                .IsUnique();
            entity.HasIndex(item => item.ReceivedAtUtc)
                .HasDatabaseName("IX_EnterpriseProviderEvents_ReceivedAtUtc");
        });
    }
}
