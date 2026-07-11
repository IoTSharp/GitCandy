using System.Data;
using System.Data.Common;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace GitCandy.Application;

/// <summary>通过串行化事务、唯一 claim 和审计事件实现名称生命周期。</summary>
internal sealed class NameManagementService(
    GitCandyDbContext dbContext,
    IOptions<GitCandyNamespaceOptions> options,
    TimeProvider timeProvider) : INameManagementService
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly GitCandyNamespaceOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<NameManagementSnapshot?> GetNamespaceSnapshotAsync(
        string namespaceSlug,
        CancellationToken cancellationToken = default)
    {
        var normalizedSlug = NamespaceSlugRules.Normalize(namespaceSlug);
        var item = await _dbContext.Namespaces
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.NormalizedSlug == normalizedSlug, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var windowStart = utcNow.AddDays(-_options.RenameWindowDays);
        var used = await CountRecentRenamesAsync(NameSubjectType.Namespace, item.Id, windowStart, cancellationToken);
        var aliases = await _dbContext.NamespaceAliases
            .AsNoTracking()
            .Where(alias => alias.NamespaceId == item.Id)
            .OrderByDescending(alias => alias.CreatedAtUtc)
            .Select(alias => new NameAliasSummary(
                alias.Id,
                alias.Slug,
                alias.CreatedAtUtc,
                alias.ExpiresAtUtc,
                alias.ReleasedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new NameManagementSnapshot(
            item.Id,
            item.Slug,
            Math.Max(0, _options.RenameLimit - used),
            windowStart,
            aliases);
    }

    /// <inheritdoc />
    public async Task<NameManagementSnapshot?> GetRepositorySnapshotAsync(
        long repositoryId,
        CancellationToken cancellationToken = default)
    {
        var item = await _dbContext.Repositories
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == repositoryId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var aliases = await _dbContext.RepositoryAliases
            .AsNoTracking()
            .Where(alias => alias.RepositoryId == item.Id)
            .OrderByDescending(alias => alias.CreatedAtUtc)
            .Select(alias => new NameAliasSummary(
                alias.Id,
                alias.Slug,
                alias.CreatedAtUtc,
                alias.ExpiresAtUtc,
                alias.ReleasedAtUtc))
            .ToArrayAsync(cancellationToken);
        return new NameManagementSnapshot(item.Id, item.Name, null, null, aliases);
    }

    /// <inheritdoc />
    public async Task<NameChangeResult> RenameNamespaceAsync(
        long namespaceId,
        string newSlug,
        string actorUserId,
        NameChangeOverride? changeOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        if (!NamespaceSlugRules.IsValidNamespaceSlug(newSlug))
        {
            return new NameChangeResult(NameChangeStatus.InvalidSlug);
        }

        if (NamespaceSlugRules.IsReserved(newSlug))
        {
            return new NameChangeResult(NameChangeStatus.Reserved);
        }

        return await ExecuteInSerializableTransactionAsync(async () =>
        {
            var item = await _dbContext.Namespaces
                .Include(value => value.User)
                .Include(value => value.Team)
                .SingleOrDefaultAsync(value => value.Id == namespaceId, cancellationToken);
            if (item is null || item.OwnerType == NamespaceOwnerType.System)
            {
                return new NameChangeResult(NameChangeStatus.NotFound);
            }

            var normalizedSlug = NamespaceSlugRules.Normalize(newSlug);
            if (string.Equals(item.NormalizedSlug, normalizedSlug, StringComparison.Ordinal))
            {
                var remaining = await GetRemainingRenamesAsync(item.Id, cancellationToken);
                return new NameChangeResult(NameChangeStatus.Succeeded, item.Slug, null, remaining);
            }

            var overrideResult = ValidateOverride(changeOverride);
            if (overrideResult is not null)
            {
                return overrideResult;
            }

            var isOverride = changeOverride is not null;
            var remainingRenames = await GetRemainingRenamesAsync(item.Id, cancellationToken);
            if (!isOverride && remainingRenames <= 0)
            {
                return new NameChangeResult(NameChangeStatus.RateLimited, RemainingRenames: 0);
            }

            if (!await PrepareClaimForCurrentNamespaceAsync(item.Id, normalizedSlug, cancellationToken))
            {
                return new NameChangeResult(NameChangeStatus.Occupied);
            }

            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            var oldSlug = item.Slug;
            var alias = new GitCandyNamespaceAlias
            {
                NamespaceId = item.Id,
                Slug = oldSlug,
                CreatedAtUtc = utcNow,
                ExpiresAtUtc = utcNow.AddDays(_options.AliasRetentionDays)
            };
            _dbContext.NamespaceAliases.Add(alias);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var oldClaim = await _dbContext.NamespaceClaims.SingleAsync(
                claim => claim.NamespaceId == item.Id,
                cancellationToken);
            oldClaim.NamespaceId = null;
            oldClaim.NamespaceAliasId = alias.Id;
            oldClaim.ClaimType = NameClaimType.Alias;
            item.Slug = newSlug.Trim();
            item.Version++;
            if (item.OwnerType == NamespaceOwnerType.User && item.User is not null)
            {
                item.User.UserName = item.Slug;
                item.User.NormalizedUserName = item.NormalizedSlug = normalizedSlug;
            }
            else if (item.OwnerType == NamespaceOwnerType.Team && item.Team is not null)
            {
                item.Team.Name = item.Slug;
                item.Team.NormalizedName = item.NormalizedSlug = normalizedSlug;
            }

            _dbContext.NamespaceClaims.Add(new GitCandyNamespaceClaim
            {
                NormalizedSlug = normalizedSlug,
                Slug = item.Slug,
                ClaimType = NameClaimType.Current,
                NamespaceId = item.Id
            });
            AddEvent(NameSubjectType.Namespace, item.Id, actorUserId, oldSlug, item.Slug, changeOverride);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new NameChangeResult(
                NameChangeStatus.Succeeded,
                item.Slug,
                alias.ExpiresAtUtc,
                isOverride ? remainingRenames : remainingRenames - 1);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<NameChangeResult> RenameRepositoryAsync(
        long repositoryId,
        string newSlug,
        string actorUserId,
        NameChangeOverride? changeOverride = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        if (!NamespaceSlugRules.IsValidRepositorySlug(newSlug))
        {
            return new NameChangeResult(NameChangeStatus.InvalidSlug);
        }

        var overrideResult = ValidateOverride(changeOverride);
        if (overrideResult is not null)
        {
            return overrideResult;
        }

        return await ExecuteInSerializableTransactionAsync(async () =>
        {
            var item = await _dbContext.Repositories.SingleOrDefaultAsync(
                value => value.Id == repositoryId,
                cancellationToken);
            if (item is null)
            {
                return new NameChangeResult(NameChangeStatus.NotFound);
            }

            var normalizedSlug = NamespaceSlugRules.Normalize(newSlug);
            if (string.Equals(item.NormalizedName, normalizedSlug, StringComparison.Ordinal))
            {
                return new NameChangeResult(NameChangeStatus.Succeeded, item.Name);
            }

            if (!await PrepareClaimForCurrentRepositoryAsync(
                item.NamespaceId,
                item.Id,
                normalizedSlug,
                cancellationToken))
            {
                return new NameChangeResult(NameChangeStatus.Occupied);
            }

            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            var oldSlug = item.Name;
            var alias = new GitCandyRepositoryAlias
            {
                NamespaceId = item.NamespaceId,
                RepositoryId = item.Id,
                Slug = oldSlug,
                CreatedAtUtc = utcNow,
                ExpiresAtUtc = utcNow.AddDays(_options.AliasRetentionDays)
            };
            _dbContext.RepositoryAliases.Add(alias);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var oldClaim = await _dbContext.RepositoryClaims.SingleAsync(
                claim => claim.RepositoryId == item.Id,
                cancellationToken);
            oldClaim.RepositoryId = null;
            oldClaim.RepositoryAliasId = alias.Id;
            oldClaim.ClaimType = NameClaimType.Alias;
            item.Name = newSlug.Trim();
            item.NormalizedName = normalizedSlug;
            _dbContext.RepositoryClaims.Add(new GitCandyRepositoryClaim
            {
                NamespaceId = item.NamespaceId,
                NormalizedSlug = normalizedSlug,
                Slug = item.Name,
                ClaimType = NameClaimType.Current,
                RepositoryId = item.Id
            });
            AddEvent(NameSubjectType.Repository, item.Id, actorUserId, oldSlug, item.Name, changeOverride);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new NameChangeResult(NameChangeStatus.Succeeded, item.Name, alias.ExpiresAtUtc);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExtendAliasAsync(
        NameSubjectType subjectType,
        long aliasId,
        DateTime expiresAtUtc,
        string actorUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        if (expiresAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc <= utcNow)
        {
            return false;
        }

        if (subjectType == NameSubjectType.Namespace)
        {
            var alias = await _dbContext.NamespaceAliases.SingleOrDefaultAsync(
                item => item.Id == aliasId && item.ReleasedAtUtc == null,
                cancellationToken);
            if (alias is null)
            {
                return false;
            }

            alias.ExpiresAtUtc = expiresAtUtc;
            AddLifecycleEvent(subjectType, alias.NamespaceId, actorUserId, alias.Slug, reason, NameEventType.AliasExtended);
        }
        else
        {
            var alias = await _dbContext.RepositoryAliases.SingleOrDefaultAsync(
                item => item.Id == aliasId && item.ReleasedAtUtc == null,
                cancellationToken);
            if (alias is null)
            {
                return false;
            }

            alias.ExpiresAtUtc = expiresAtUtc;
            AddLifecycleEvent(subjectType, alias.RepositoryId, actorUserId, alias.Slug, reason, NameEventType.AliasExtended);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> ReleaseExpiredAliasesAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Alias cleanup time must be UTC.", nameof(utcNow));
        }

        var namespaceAliases = await _dbContext.NamespaceAliases
            .Where(alias => alias.ReleasedAtUtc == null && alias.ExpiresAtUtc <= utcNow)
            .ToArrayAsync(cancellationToken);
        var repositoryAliases = await _dbContext.RepositoryAliases
            .Where(alias => alias.ReleasedAtUtc == null && alias.ExpiresAtUtc <= utcNow)
            .ToArrayAsync(cancellationToken);
        if (namespaceAliases.Length == 0 && repositoryAliases.Length == 0)
        {
            return 0;
        }

        var namespaceAliasIds = namespaceAliases.Select(alias => alias.Id).ToArray();
        var repositoryAliasIds = repositoryAliases.Select(alias => alias.Id).ToArray();
        var namespaceClaims = await _dbContext.NamespaceClaims
            .Where(claim => claim.NamespaceAliasId != null
                && namespaceAliasIds.Contains(claim.NamespaceAliasId.Value))
            .ToArrayAsync(cancellationToken);
        var repositoryClaims = await _dbContext.RepositoryClaims
            .Where(claim => claim.RepositoryAliasId != null
                && repositoryAliasIds.Contains(claim.RepositoryAliasId.Value))
            .ToArrayAsync(cancellationToken);
        _dbContext.NamespaceClaims.RemoveRange(namespaceClaims);
        _dbContext.RepositoryClaims.RemoveRange(repositoryClaims);
        foreach (var alias in namespaceAliases)
        {
            alias.ReleasedAtUtc = utcNow;
            AddLifecycleEvent(NameSubjectType.Namespace, alias.NamespaceId, null, alias.Slug, "Alias retention expired.", NameEventType.AliasReleased);
        }

        foreach (var alias in repositoryAliases)
        {
            alias.ReleasedAtUtc = utcNow;
            AddLifecycleEvent(NameSubjectType.Repository, alias.RepositoryId, null, alias.Slug, "Alias retention expired.", NameEventType.AliasReleased);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return namespaceAliases.Length + repositoryAliases.Length;
    }

    private async Task<NameChangeResult> ExecuteInSerializableTransactionAsync(
        Func<Task<NameChangeResult>> operation,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        try
        {
            if (_dbContext.Database.IsRelational())
            {
                transaction = await _dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            var result = await operation();
            if (transaction is not null)
            {
                if (result.Status == NameChangeStatus.Succeeded)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _dbContext.ChangeTracker.Clear();
                }
            }

            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();
            return new NameChangeResult(NameChangeStatus.Conflict);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();
            return new NameChangeResult(NameChangeStatus.Occupied);
        }
        catch (DbException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _dbContext.ChangeTracker.Clear();
            return new NameChangeResult(NameChangeStatus.Conflict);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<bool> PrepareClaimForCurrentNamespaceAsync(
        long namespaceId,
        string normalizedSlug,
        CancellationToken cancellationToken)
    {
        var claim = await _dbContext.NamespaceClaims
            .Include(item => item.NamespaceAlias)
            .SingleOrDefaultAsync(item => item.NormalizedSlug == normalizedSlug, cancellationToken);
        if (claim is null)
        {
            return true;
        }

        if (claim.ClaimType != NameClaimType.Alias
            || claim.NamespaceAlias?.NamespaceId != namespaceId)
        {
            return false;
        }

        claim.NamespaceAlias.ReleasedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        _dbContext.NamespaceClaims.Remove(claim);
        return true;
    }

    private async Task<bool> PrepareClaimForCurrentRepositoryAsync(
        long namespaceId,
        long repositoryId,
        string normalizedSlug,
        CancellationToken cancellationToken)
    {
        var claim = await _dbContext.RepositoryClaims
            .Include(item => item.RepositoryAlias)
            .SingleOrDefaultAsync(
                item => item.NamespaceId == namespaceId && item.NormalizedSlug == normalizedSlug,
                cancellationToken);
        if (claim is null)
        {
            return true;
        }

        if (claim.ClaimType != NameClaimType.Alias
            || claim.RepositoryAlias?.RepositoryId != repositoryId)
        {
            return false;
        }

        claim.RepositoryAlias.ReleasedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        _dbContext.RepositoryClaims.Remove(claim);
        return true;
    }

    private async Task<int> GetRemainingRenamesAsync(long namespaceId, CancellationToken cancellationToken)
    {
        var windowStart = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-_options.RenameWindowDays);
        var used = await CountRecentRenamesAsync(NameSubjectType.Namespace, namespaceId, windowStart, cancellationToken);
        return Math.Max(0, _options.RenameLimit - used);
    }

    private Task<int> CountRecentRenamesAsync(
        NameSubjectType subjectType,
        long subjectId,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return _dbContext.RenameEvents.CountAsync(
            item => item.SubjectType == subjectType
                && item.SubjectId == subjectId
                && item.EventType == NameEventType.Renamed
                && !item.IsOverride
                && item.OccurredAtUtc >= windowStart,
            cancellationToken);
    }

    private static NameChangeResult? ValidateOverride(NameChangeOverride? changeOverride)
    {
        if (changeOverride is null)
        {
            return null;
        }

        return !changeOverride.Confirmed || string.IsNullOrWhiteSpace(changeOverride.Reason)
            ? new NameChangeResult(NameChangeStatus.ConfirmationRequired)
            : null;
    }

    private void AddEvent(
        NameSubjectType subjectType,
        long subjectId,
        string actorUserId,
        string oldSlug,
        string newSlug,
        NameChangeOverride? changeOverride)
    {
        _dbContext.RenameEvents.Add(new GitCandyRenameEvent
        {
            EventType = NameEventType.Renamed,
            SubjectType = subjectType,
            SubjectId = subjectId,
            ActorUserId = actorUserId,
            OldSlug = oldSlug,
            NewSlug = newSlug,
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            Reason = changeOverride?.Reason.Trim(),
            IsOverride = changeOverride is not null
        });
    }

    private void AddLifecycleEvent(
        NameSubjectType subjectType,
        long subjectId,
        string? actorUserId,
        string slug,
        string reason,
        NameEventType eventType)
    {
        _dbContext.RenameEvents.Add(new GitCandyRenameEvent
        {
            EventType = eventType,
            SubjectType = subjectType,
            SubjectId = subjectId,
            ActorUserId = actorUserId,
            OldSlug = slug,
            NewSlug = slug,
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            Reason = reason.Trim()
        });
    }
}
