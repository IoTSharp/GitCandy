using System.Data;
using GitCandy.Data;
using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>在全局 claim 约束下幂等创建用户和团队 namespace。</summary>
internal sealed class NamespaceProvisioningService(
    GitCandyDbContext dbContext,
    TimeProvider timeProvider) : INamespaceProvisioningService
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<long?> EnsureUserNamespaceAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var existingId = await _dbContext.Namespaces
            .Where(item => item.UserId == userId)
            .Select(item => (long?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingId is not null)
        {
            return existingId;
        }

        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        var slug = user?.UserName?.Trim();
        return user is null || !CanClaim(slug)
            ? null
            : await CreateNamespaceAsync(NamespaceOwnerType.User, user.Id, null, slug!, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long?> EnsureTeamNamespaceAsync(
        long teamId,
        CancellationToken cancellationToken = default)
    {
        var existingId = await _dbContext.Namespaces
            .Where(item => item.TeamId == teamId)
            .Select(item => (long?)item.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingId is not null)
        {
            return existingId;
        }

        var team = await _dbContext.Teams.SingleOrDefaultAsync(item => item.Id == teamId, cancellationToken);
        return team is null || !CanClaim(team.Name)
            ? null
            : await CreateNamespaceAsync(NamespaceOwnerType.Team, null, team.Id, team.Name, cancellationToken);
    }

    private async Task<long?> CreateNamespaceAsync(
        NamespaceOwnerType ownerType,
        string? userId,
        long? teamId,
        string slug,
        CancellationToken cancellationToken)
    {
        var hasAmbientTransaction = _dbContext.Database.CurrentTransaction is not null;
        await using var transaction = _dbContext.Database.IsRelational() && !hasAmbientTransaction
            ? await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var normalizedSlug = NamespaceSlugRules.Normalize(slug);
        if (await _dbContext.NamespaceClaims.AnyAsync(
            item => item.NormalizedSlug == normalizedSlug,
            cancellationToken))
        {
            return null;
        }

        var item = new GitCandyNamespace
        {
            OwnerType = ownerType,
            UserId = userId,
            TeamId = teamId,
            Slug = slug,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };
        _dbContext.Namespaces.Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.NamespaceClaims.Add(new GitCandyNamespaceClaim
        {
            NamespaceId = item.Id,
            Slug = slug,
            NormalizedSlug = normalizedSlug,
            ClaimType = NameClaimType.Current
        });
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return item.Id;
        }
        catch (DbUpdateException)
        {
            if (hasAmbientTransaction)
            {
                throw;
            }

            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();
            return null;
        }
    }

    private static bool CanClaim(string? slug)
    {
        return NamespaceSlugRules.IsValidNamespaceSlug(slug)
            && !NamespaceSlugRules.IsReserved(slug!);
    }
}
