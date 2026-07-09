using GitCandy.Data.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Data;

/// <summary>
/// GitCandy 面向 ASP.NET Core Identity 和新领域模型的 EF Core 上下文。
/// </summary>
public sealed class GitCandyDbContext(DbContextOptions<GitCandyDbContext> options)
    : IdentityDbContext<GitCandyUser>(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        builder.Entity<GitCandyUser>(entity =>
        {
            entity.Property(user => user.DisplayName)
                .HasMaxLength(128);

            entity.Property(user => user.Description)
                .HasMaxLength(512);
        });
    }
}
