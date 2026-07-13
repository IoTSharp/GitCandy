using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data.Configuration;

internal static class CredentialModelConfiguration
{
    public static void ConfigureCredentialModel(this ModelBuilder builder)
    {
        builder.Entity<GitCandyPersonalAccessToken>(entity =>
        {
            entity.ToTable("PersonalAccessTokens");
            entity.HasKey(token => token.Id);
            entity.Property(token => token.Id).ValueGeneratedOnAdd();
            entity.Property(token => token.UserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(token => token.Name).IsRequired().HasMaxLength(SchemaLimits.CredentialName);
            entity.Property(token => token.TokenHash).IsRequired().IsFixedLength().HasMaxLength(SchemaLimits.Sha256Hash);
            entity.Property(token => token.TokenPrefix).IsRequired().HasMaxLength(SchemaLimits.TokenPrefix);
            entity.Property(token => token.Scopes).IsRequired().HasMaxLength(SchemaLimits.CredentialScopes);
            entity.Property(token => token.CreatedAtUtc).IsRequired();
            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(token => token.TokenHash)
                .HasDatabaseName("IX_PersonalAccessTokens_TokenHash")
                .IsUnique();
            entity.HasIndex(token => new { token.UserId, token.RevokedAtUtc })
                .HasDatabaseName("IX_PersonalAccessTokens_UserId_RevokedAtUtc");
        });

        builder.Entity<GitCandyCredentialAuditEvent>(entity =>
        {
            entity.ToTable("CredentialAuditEvents");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.Id).ValueGeneratedOnAdd();
            entity.Property(audit => audit.CredentialKind).IsRequired().HasMaxLength(SchemaLimits.CredentialKind);
            entity.Property(audit => audit.ActorUserId).HasMaxLength(SchemaLimits.IdentityKey);
            entity.Property(audit => audit.Action).IsRequired().HasMaxLength(SchemaLimits.AuditAction);
            entity.Property(audit => audit.Outcome).IsRequired().HasMaxLength(SchemaLimits.AuditOutcome);
            entity.Property(audit => audit.Detail).IsRequired().HasMaxLength(SchemaLimits.AuditDetail);
            entity.Property(audit => audit.OccurredAtUtc).IsRequired();
            entity.HasIndex(audit => new { audit.CredentialKind, audit.CredentialId, audit.OccurredAtUtc })
                .HasDatabaseName("IX_CredentialAuditEvents_Credential_OccurredAtUtc");
            entity.HasIndex(audit => new { audit.RepositoryId, audit.OccurredAtUtc })
                .HasDatabaseName("IX_CredentialAuditEvents_RepositoryId_OccurredAtUtc");
        });

        builder.Entity<GitCandyDeployKey>(entity =>
        {
            entity.ToTable("DeployKeys");
            entity.HasKey(key => key.Id);
            entity.Property(key => key.Id).ValueGeneratedOnAdd();
            entity.Property(key => key.Name).IsRequired().HasMaxLength(SchemaLimits.CredentialName);
            entity.Property(key => key.KeyType).IsRequired().HasMaxLength(SchemaLimits.SshKeyType);
            entity.Property(key => key.Fingerprint).IsRequired().IsFixedLength().HasMaxLength(SchemaLimits.SshFingerprint);
            entity.Property(key => key.PublicKey).IsRequired().HasMaxLength(SchemaLimits.SshPublicKey);
            entity.Property(key => key.CanWrite).IsRequired();
            entity.Property(key => key.CreatedAtUtc).IsRequired();
            entity.Property(key => key.CreatedByUserId).IsRequired().HasMaxLength(SchemaLimits.IdentityKey);
            entity.HasOne(key => key.Repository)
                .WithMany(repository => repository.DeployKeys)
                .HasForeignKey(key => key.RepositoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(key => key.Fingerprint)
                .HasDatabaseName("IX_DeployKeys_Fingerprint")
                .IsUnique();
            entity.HasIndex(key => key.RepositoryId)
                .HasDatabaseName("IX_DeployKeys_RepositoryId");
        });

        builder.Entity<GitCandySshFingerprintClaim>(entity =>
        {
            entity.ToTable("SshFingerprintClaims");
            entity.HasKey(claim => claim.Fingerprint);
            entity.Property(claim => claim.Fingerprint).IsFixedLength().HasMaxLength(SchemaLimits.SshFingerprint);
            entity.Property(claim => claim.CredentialKind).IsRequired().HasMaxLength(SchemaLimits.CredentialKind);
            entity.Property(claim => claim.ClaimedAtUtc).IsRequired();
        });
    }
}
