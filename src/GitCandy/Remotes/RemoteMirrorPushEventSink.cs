using System.Data;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Remotes;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Web.Remotes;

/// <summary>将成功 push 的 ref 更新按 mirror/ref 合并到持久化 pending 表。</summary>
public sealed class RemoteMirrorPushEventSink(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    TimeProvider timeProvider) : IRemoteMirrorPushEventSink
{
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task EnqueueAsync(
        long repositoryId,
        IReadOnlyList<RemoteMirrorRefEvent> updates,
        CancellationToken cancellationToken = default)
    {
        if (repositoryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(repositoryId));
        }
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count is 0 or > 1024)
        {
            throw new ArgumentException("A push event requires between 1 and 1024 ref updates.", nameof(updates));
        }
        if (updates.Any(static item => !IsValidObjectId(item.OldObjectId)
                || !IsValidObjectId(item.NewObjectId)))
        {
            throw new ArgumentException("A push event contains an invalid Git object ID.", nameof(updates));
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var mirrors = await dbContext.RepositoryMirrors
            .Include(item => item.Connection)
            .Where(item => item.RepositoryId == repositoryId
                && item.Direction == RemoteMirrorDirection.Push
                && item.IsEnabled
                && item.Connection != null
                && item.Connection.IsEnabled)
            .ToArrayAsync(cancellationToken);
        if (mirrors.Length == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var protectedPatterns = await dbContext.BranchProtectionRules.AsNoTracking()
            .Where(item => item.RepositoryId == repositoryId)
            .Select(item => item.Pattern)
            .ToArrayAsync(cancellationToken);
        var mirrorIds = mirrors.Select(item => item.Id).ToArray();
        var referenceNames = updates.Select(item => item.ReferenceName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var pending = await dbContext.RemoteMirrorRefUpdates
            .Where(item => mirrorIds.Contains(item.MirrorId)
                && referenceNames.Contains(item.ReferenceName))
            .ToDictionaryAsync(item => (item.MirrorId, item.ReferenceName), cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var mirror in mirrors)
        {
            if (!RemoteMirrorRefFilter.TryCreate(
                    mirror.RefFilterKind,
                    mirror.RefFilterPattern,
                    protectedPatterns,
                    out var filter))
            {
                mirror.Status = RemoteMirrorStatus.Failed;
                mirror.LastErrorCode = RemoteMirrorErrorCodes.InvalidConfiguration;
                mirror.UpdatedAtUtc = now;
                continue;
            }

            var enqueued = false;
            foreach (var update in updates.Where(item => filter!.Matches(item.ReferenceName)))
            {
                var key = (mirror.Id, update.ReferenceName);
                if (pending.TryGetValue(key, out var existing))
                {
                    existing.OldObjectId = update.OldObjectId.ToLowerInvariant();
                    existing.NewObjectId = update.NewObjectId.ToLowerInvariant();
                    existing.Generation++;
                    existing.UpdatedAtUtc = now;
                }
                else
                {
                    existing = new GitCandyRemoteMirrorRefUpdate
                    {
                        MirrorId = mirror.Id,
                        ReferenceName = update.ReferenceName,
                        OldObjectId = update.OldObjectId.ToLowerInvariant(),
                        NewObjectId = update.NewObjectId.ToLowerInvariant(),
                        Generation = 1,
                        EnqueuedAtUtc = now,
                        UpdatedAtUtc = now
                    };
                    dbContext.RemoteMirrorRefUpdates.Add(existing);
                    pending.Add(key, existing);
                }
                enqueued = true;
            }

            if (enqueued && mirror.Status != RemoteMirrorStatus.Paused)
            {
                mirror.Status = RemoteMirrorStatus.Pending;
                mirror.LastErrorCode = null;
                mirror.UpdatedAtUtc = now;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsValidObjectId(string objectId) =>
        objectId.Length is 40 or 64 && objectId.All(Uri.IsHexDigit);
}
