using System.Data;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Remotes;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>使用 EF Core 保存触发、租约、重试和重启恢复状态的 mirror job 队列。</summary>
public sealed class RemoteMirrorJobQueue(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    TimeProvider timeProvider) : IRemoteMirrorJobQueue
{
    private const int MaxBatchSize = 100;
    private const int MaxEnqueueAttempts = 3;
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task EnqueueAsync(
        long mirrorId,
        RemoteMirrorJobTrigger trigger,
        DateTimeOffset? availableAt = null,
        CancellationToken cancellationToken = default)
    {
        if (mirrorId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mirrorId));
        }
        if (trigger == RemoteMirrorJobTrigger.None || (trigger & ~AllTriggers) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trigger));
        }

        var now = _timeProvider.GetUtcNow();
        var normalizedAvailableAt = availableAt is null || availableAt < now
            ? now.UtcDateTime
            : availableAt.Value.UtcDateTime;
        for (var attempt = 0; attempt < MaxEnqueueAttempts; attempt++)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var job = await dbContext.RemoteMirrorJobs.SingleOrDefaultAsync(
                item => item.MirrorId == mirrorId,
                cancellationToken);
            if (job is null)
            {
                dbContext.RemoteMirrorJobs.Add(new GitCandyRemoteMirrorJob
                {
                    MirrorId = mirrorId,
                    State = RemoteMirrorJobState.Pending,
                    Triggers = trigger,
                    RequestedGeneration = 1,
                    AvailableAtUtc = normalizedAvailableAt,
                    CreatedAtUtc = now.UtcDateTime,
                    UpdatedAtUtc = now.UtcDateTime,
                    Version = 1
                });
            }
            else
            {
                var hasOutstandingRequest = job.State is RemoteMirrorJobState.Pending or RemoteMirrorJobState.Leased
                    && job.ProcessedGeneration < job.RequestedGeneration;
                var repeatedSchedule = trigger == RemoteMirrorJobTrigger.Schedule
                    && job.State is RemoteMirrorJobState.Pending or RemoteMirrorJobState.Leased
                    && (job.Triggers & RemoteMirrorJobTrigger.Schedule) != 0;
                if (!repeatedSchedule)
                {
                    job.RequestedGeneration++;
                }
                job.Triggers |= trigger;
                job.UpdatedAtUtc = now.UtcDateTime;
                job.Version++;
                if (job.State != RemoteMirrorJobState.Leased)
                {
                    job.State = RemoteMirrorJobState.Pending;
                    job.AvailableAtUtc = hasOutstandingRequest
                        ? Min(job.AvailableAtUtc, normalizedAvailableAt)
                        : normalizedAvailableAt;
                    job.CancellationRequestedAtUtc = null;
                    job.LastCompletedAtUtc = null;
                    job.LeaseOwner = null;
                    job.LeaseExpiresAtUtc = null;
                }
            }

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (attempt + 1 < MaxEnqueueAttempts)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        throw new InvalidOperationException("The mirror job could not be enqueued after concurrent updates.");
    }

    public async Task<int> EnqueueDuePullMirrorsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, MaxBatchSize);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await dbContext.RepositoryMirrors.AsNoTracking()
            .Where(item => item.Direction == RemoteMirrorDirection.Pull
                && item.IsEnabled
                && item.ScheduleEnabled
                && item.ScheduleIntervalMinutes != null
                && item.Status != RemoteMirrorStatus.Paused
                && item.Connection != null
                && item.Connection.IsEnabled)
            .OrderBy(item => item.LastAttemptedAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => new { item.Id, item.LastAttemptedAtUtc, item.ScheduleIntervalMinutes })
            .Take(limit * 4)
            .ToArrayAsync(cancellationToken);
        var dueIds = candidates
            .Where(item => item.LastAttemptedAtUtc is null
                || item.LastAttemptedAtUtc.Value.AddMinutes(item.ScheduleIntervalMinutes!.Value) <= now)
            .Take(limit)
            .Select(item => item.Id)
            .ToArray();
        foreach (var mirrorId in dueIds)
        {
            await EnqueueAsync(mirrorId, RemoteMirrorJobTrigger.Schedule, cancellationToken: cancellationToken);
        }
        return dueIds.Length;
    }

    public async Task<int> EnqueueRecoveryCandidatesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, MaxBatchSize);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await dbContext.RepositoryMirrors.AsNoTracking()
            .Where(item => item.IsEnabled
                && item.Status != RemoteMirrorStatus.Paused
                && item.Connection != null
                && item.Connection.IsEnabled
                && (item.Job == null && item.Status == RemoteMirrorStatus.Pending
                    || item.Direction == RemoteMirrorDirection.Push
                        && item.PendingRefUpdates.Count > 0
                        && item.Job != null
                        && item.Job.State == RemoteMirrorJobState.Succeeded))
            .OrderBy(item => item.UpdatedAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                HasPendingPush = item.Direction == RemoteMirrorDirection.Push
                    && item.PendingRefUpdates.Count > 0
            })
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            await EnqueueAsync(
                candidate.Id,
                candidate.HasPendingPush
                    ? RemoteMirrorJobTrigger.Push | RemoteMirrorJobTrigger.Recovery
                    : RemoteMirrorJobTrigger.Initial | RemoteMirrorJobTrigger.Recovery,
                cancellationToken: cancellationToken);
        }
        return candidates.Length;
    }

    public async Task<IReadOnlyList<RemoteMirrorJobLease>> AcquireAsync(
        string leaseOwner,
        int limit,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (leaseOwner.Length > SchemaLimits.RemoteLeaseOwner)
        {
            throw new ArgumentException("The mirror lease owner is too long.", nameof(leaseOwner));
        }
        if (leaseDuration < TimeSpan.FromSeconds(30) || leaseDuration > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        limit = Math.Clamp(limit, 1, MaxBatchSize);
        await RecoverExpiredLeasesAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();
        await using var candidateContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var candidateIds = await candidateContext.RemoteMirrorJobs.AsNoTracking()
            .Where(item => item.State == RemoteMirrorJobState.Pending
                && item.AvailableAtUtc <= now.UtcDateTime
                && item.Mirror != null
                && item.Mirror.IsEnabled
                && item.Mirror.Status != RemoteMirrorStatus.Paused
                && item.Mirror.Connection != null
                && item.Mirror.Connection.IsEnabled)
            .OrderBy(item => item.AvailableAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .Take(limit * 2)
            .ToArrayAsync(cancellationToken);

        var leases = new List<RemoteMirrorJobLease>(limit);
        foreach (var jobId in candidateIds)
        {
            if (leases.Count == limit)
            {
                break;
            }

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var job = await dbContext.RemoteMirrorJobs.SingleOrDefaultAsync(
                item => item.Id == jobId,
                cancellationToken);
            if (job is null
                || job.State != RemoteMirrorJobState.Pending
                || job.AvailableAtUtc > now.UtcDateTime)
            {
                continue;
            }

            job.State = RemoteMirrorJobState.Leased;
            job.LeaseOwner = leaseOwner;
            job.LeaseExpiresAtUtc = now.Add(leaseDuration).UtcDateTime;
            job.CancellationRequestedAtUtc = null;
            job.AttemptCount++;
            job.LastStartedAtUtc = now.UtcDateTime;
            job.UpdatedAtUtc = now.UtcDateTime;
            job.Version++;
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                continue;
            }

            leases.Add(new RemoteMirrorJobLease(
                job.Id,
                job.MirrorId,
                job.RequestedGeneration,
                job.AttemptCount,
                job.Triggers,
                now.Add(leaseDuration)));
        }

        return leases;
    }

    public async Task CompleteAsync(
        long jobId,
        string leaseOwner,
        RemoteMirrorJobCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        ArgumentNullException.ThrowIfNull(completion);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var job = await dbContext.RemoteMirrorJobs.SingleOrDefaultAsync(
            item => item.Id == jobId,
            cancellationToken);
        if (job is null
            || job.State != RemoteMirrorJobState.Leased
            || !string.Equals(job.LeaseOwner, leaseOwner, StringComparison.Ordinal))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        job.ProcessedGeneration = Math.Max(job.ProcessedGeneration, completion.RequestedGeneration);
        job.LastErrorCode = completion.ErrorCode;
        job.LastCompletedAtUtc = now.UtcDateTime;
        job.UpdatedAtUtc = now.UtcDateTime;
        job.LeaseOwner = null;
        job.LeaseExpiresAtUtc = null;
        if (job.CancellationRequestedAtUtc is not null || completion.Canceled)
        {
            job.State = RemoteMirrorJobState.Canceled;
            job.ProcessedGeneration = job.RequestedGeneration;
        }
        else if (job.RequestedGeneration > completion.RequestedGeneration)
        {
            job.State = RemoteMirrorJobState.Pending;
            job.AvailableAtUtc = now.UtcDateTime;
        }
        else if (completion.Succeeded)
        {
            job.State = RemoteMirrorJobState.Succeeded;
            job.AttemptCount = 0;
            job.LastErrorCode = null;
        }
        else if (completion.Retry)
        {
            job.State = RemoteMirrorJobState.Pending;
            job.AvailableAtUtc = now.Add(completion.RetryDelay).UtcDateTime;
        }
        else
        {
            job.State = RemoteMirrorJobState.Failed;
        }
        job.Version++;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(
        long jobId,
        string leaseOwner,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var job = await dbContext.RemoteMirrorJobs.SingleOrDefaultAsync(
            item => item.Id == jobId,
            cancellationToken);
        if (job is null
            || job.State != RemoteMirrorJobState.Leased
            || !string.Equals(job.LeaseOwner, leaseOwner, StringComparison.Ordinal))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var canceled = job.CancellationRequestedAtUtc is not null;
        job.State = canceled ? RemoteMirrorJobState.Canceled : RemoteMirrorJobState.Pending;
        job.ProcessedGeneration = canceled ? job.RequestedGeneration : job.ProcessedGeneration;
        job.Triggers = canceled ? job.Triggers : job.Triggers | RemoteMirrorJobTrigger.Recovery;
        job.AvailableAtUtc = now;
        job.LeaseOwner = null;
        job.LeaseExpiresAtUtc = null;
        job.LastErrorCode = canceled ? RemoteMirrorErrorCodes.Canceled : job.LastErrorCode;
        job.LastCompletedAtUtc = canceled ? now : job.LastCompletedAtUtc;
        job.UpdatedAtUtc = now;
        job.Version++;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RequestCancellationAsync(
        long jobId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var job = await dbContext.RemoteMirrorJobs.SingleOrDefaultAsync(
            item => item.Id == jobId,
            cancellationToken);
        if (job is null || job.State is RemoteMirrorJobState.Succeeded or RemoteMirrorJobState.Canceled)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        job.CancellationRequestedAtUtc = now;
        job.UpdatedAtUtc = now;
        if (job.State != RemoteMirrorJobState.Leased)
        {
            job.State = RemoteMirrorJobState.Canceled;
            job.ProcessedGeneration = job.RequestedGeneration;
            job.LastCompletedAtUtc = now;
            job.LeaseOwner = null;
            job.LeaseExpiresAtUtc = null;
        }
        job.Version++;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RetryAsync(
        long jobId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var job = await dbContext.RemoteMirrorJobs.SingleOrDefaultAsync(
            item => item.Id == jobId,
            cancellationToken);
        if (job is null || job.State is not (RemoteMirrorJobState.Failed or RemoteMirrorJobState.Canceled))
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        job.State = RemoteMirrorJobState.Pending;
        job.Triggers |= RemoteMirrorJobTrigger.Manual;
        job.RequestedGeneration++;
        job.AttemptCount = 0;
        job.AvailableAtUtc = now;
        job.CancellationRequestedAtUtc = null;
        job.LastErrorCode = null;
        job.LastCompletedAtUtc = null;
        job.UpdatedAtUtc = now;
        job.Version++;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<RemoteMirrorJobSummary>> GetForRepositoryAsync(
        long repositoryId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, MaxBatchSize);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var jobs = await dbContext.RemoteMirrorJobs.AsNoTracking()
            .Where(item => item.Mirror != null && item.Mirror.RepositoryId == repositoryId)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        return jobs.Select(static item => new RemoteMirrorJobSummary(
            item.Id,
            item.MirrorId,
            item.State,
            item.Triggers,
            item.AttemptCount,
            ToDateTimeOffset(item.AvailableAtUtc),
            ToDateTimeOffset(item.LeaseExpiresAtUtc),
            item.CancellationRequestedAtUtc is not null,
            item.LastErrorCode,
            ToDateTimeOffset(item.LastStartedAtUtc),
            ToDateTimeOffset(item.LastCompletedAtUtc),
            ToDateTimeOffset(item.UpdatedAtUtc))).ToArray();
    }

    private async Task RecoverExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var expired = await dbContext.RemoteMirrorJobs
            .Where(item => item.State == RemoteMirrorJobState.Leased
                && item.LeaseExpiresAtUtc != null
                && item.LeaseExpiresAtUtc <= now)
            .ToArrayAsync(cancellationToken);
        foreach (var job in expired)
        {
            var canceled = job.CancellationRequestedAtUtc is not null;
            job.State = canceled ? RemoteMirrorJobState.Canceled : RemoteMirrorJobState.Pending;
            job.ProcessedGeneration = canceled ? job.RequestedGeneration : job.ProcessedGeneration;
            job.Triggers = canceled ? job.Triggers : job.Triggers | RemoteMirrorJobTrigger.Recovery;
            job.AvailableAtUtc = now;
            job.LeaseOwner = null;
            job.LeaseExpiresAtUtc = null;
            job.LastErrorCode = canceled
                ? RemoteMirrorErrorCodes.Canceled
                : RemoteMirrorErrorCodes.LeaseExpired;
            job.LastCompletedAtUtc = canceled ? now : job.LastCompletedAtUtc;
            job.UpdatedAtUtc = now;
            job.Version++;
        }
        if (expired.Length > 0)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another instance recovered the same lease first.
            }
        }
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value) => value is null
        ? null
        : ToDateTimeOffset(value.Value);

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;

    private const RemoteMirrorJobTrigger AllTriggers =
        RemoteMirrorJobTrigger.Initial
        | RemoteMirrorJobTrigger.Schedule
        | RemoteMirrorJobTrigger.Push
        | RemoteMirrorJobTrigger.Webhook
        | RemoteMirrorJobTrigger.Manual
        | RemoteMirrorJobTrigger.Recovery;
}
