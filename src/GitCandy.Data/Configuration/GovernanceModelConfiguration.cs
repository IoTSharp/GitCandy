using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class GovernanceModelConfiguration
{
    public static void ConfigureGovernanceModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyBranchProtectionRule>(entity =>
        {
            entity.ToTable("BranchProtectionRules");
            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.Id).ValueGeneratedOnAdd();
            entity.Property(rule => rule.Pattern).IsRequired().HasMaxLength(SchemaLimits.GitRefName);
            entity.Property(rule => rule.PushAccess).IsRequired();
            entity.Property(rule => rule.MergeAccess).IsRequired();
            entity.Property(rule => rule.AllowForcePushes).IsRequired();
            entity.Property(rule => rule.AllowDeletions).IsRequired();
            entity.Property(rule => rule.AllowAdministratorBypass).IsRequired();
            entity.Property(rule => rule.CreatedAtUtc).IsRequired();
            entity.Property(rule => rule.UpdatedAtUtc).IsRequired();
            entity.HasOne(rule => rule.Repository)
                .WithMany(repository => repository.BranchProtectionRules)
                .HasForeignKey(rule => rule.RepositoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(rule => new { rule.RepositoryId, rule.Pattern })
                .HasDatabaseName("IX_BranchProtectionRules_RepositoryId_Pattern")
                .IsUnique();
        });

        builder.Entity<GitCandyGovernanceAuditEvent>(entity =>
        {
            entity.ToTable("GovernanceAuditEvents");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.Id).ValueGeneratedOnAdd();
            entity.Property(audit => audit.ActorUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(audit => audit.Action).IsRequired().HasMaxLength(SchemaLimits.AuditAction);
            entity.Property(audit => audit.Outcome).IsRequired().HasMaxLength(SchemaLimits.AuditOutcome);
            entity.Property(audit => audit.ReferenceName).IsRequired().HasMaxLength(SchemaLimits.GitRefName);
            entity.Property(audit => audit.Detail).IsRequired().HasMaxLength(SchemaLimits.AuditDetail);
            entity.Property(audit => audit.OccurredAtUtc).IsRequired();
            entity.HasIndex(audit => new { audit.RepositoryId, audit.OccurredAtUtc })
                .HasDatabaseName("IX_GovernanceAuditEvents_RepositoryId_OccurredAtUtc");
        });

        builder.Entity<GitCandyBranchProtectionRequiredCheck>(entity =>
        {
            entity.ToTable("BranchProtectionRequiredChecks");
            entity.HasKey(item => new { item.RuleId, item.Context });
            entity.Property(item => item.Context).HasMaxLength(SchemaLimits.CheckContext);
            entity.HasOne(item => item.Rule)
                .WithMany(rule => rule.RequiredChecks)
                .HasForeignKey(item => item.RuleId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
