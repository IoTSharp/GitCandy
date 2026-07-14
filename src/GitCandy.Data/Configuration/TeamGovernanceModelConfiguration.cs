using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class TeamGovernanceModelConfiguration
{
    public static void ConfigureTeamGovernanceModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyTeamAuditEvent>(entity =>
        {
            entity.ToTable("TeamAuditEvents");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.TeamName).IsRequired().HasMaxLength(SchemaLimits.TeamName);
            entity.Property(item => item.ActorUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(item => item.ActorName).IsRequired().HasMaxLength(SchemaLimits.IntegrationActorName);
            entity.Property(item => item.Action).IsRequired().HasMaxLength(SchemaLimits.AuditAction);
            entity.Property(item => item.Outcome).IsRequired().HasMaxLength(SchemaLimits.AuditOutcome);
            entity.Property(item => item.Subject).IsRequired().HasMaxLength(SchemaLimits.IdentityStoreKey);
            entity.Property(item => item.Detail).IsRequired().HasMaxLength(SchemaLimits.AuditDetail);
            entity.Property(item => item.OccurredAtUtc).IsRequired();
            entity.HasIndex(item => new { item.TeamId, item.OccurredAtUtc })
                .HasDatabaseName("IX_TeamAuditEvents_TeamId_OccurredAtUtc");
        });
    }
}
