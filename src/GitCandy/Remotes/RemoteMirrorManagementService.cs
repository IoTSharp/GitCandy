using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Remotes;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Web.Remotes;

/// <summary>实现仓库 owner 的 mirror 运维操作，并把执行请求统一写入 durable queue。</summary>
public sealed class RemoteMirrorManagementService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    IRemoteMirrorJobQueue jobQueue,
    IRemoteMirrorJobDispatcher dispatcher,
    TimeProvider timeProvider) : IRemoteMirrorManagementService
{
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly IRemoteMirrorJobQueue _jobQueue = jobQueue;
    private readonly IRemoteMirrorJobDispatcher _dispatcher = dispatcher;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<IReadOnlyList<RemoteMirrorSummary>> GetForRepositoryAsync(
        long repositoryId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var mirrors = await dbContext.RepositoryMirrors.AsNoTracking()
            .Include(item => item.Connection)
            .Where(item => item.RepositoryId == repositoryId)
            .OrderBy(item => item.Direction)
            .ThenBy(item => item.RemoteOwnerLogin)
            .ThenBy(item => item.RemoteRepositoryName)
            .ToArrayAsync(cancellationToken);
        return mirrors.Where(item => item.Connection is not null).Select(static item =>
            new RemoteMirrorSummary(
                item.Id,
                item.RepositoryId,
                item.ConnectionId,
                item.Connection!.Provider,
                item.RemoteRepositoryId,
                $"{item.RemoteOwnerLogin}/{item.RemoteRepositoryName}",
                new Uri(item.RemoteGitUrl, UriKind.Absolute),
                item.Direction,
                item.RefFilterKind,
                item.RefFilterPattern,
                item.ScheduleIntervalMinutes,
                item.ScheduleEnabled,
                item.DivergencePolicy,
                item.Prune,
                item.IsEnabled,
                item.Status,
                item.LastErrorCode,
                ToDateTimeOffset(item.LastAttemptedAtUtc),
                ToDateTimeOffset(item.LastSucceededAtUtc),
                ToDateTimeOffset(item.UpdatedAtUtc)!.Value)).ToArray();
    }

    public async Task<bool> SetPausedAsync(
        long repositoryId,
        long mirrorId,
        string actorUserId,
        bool paused,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var mirror = await dbContext.RepositoryMirrors
            .Include(item => item.Job)
            .SingleOrDefaultAsync(item => item.Id == mirrorId && item.RepositoryId == repositoryId, cancellationToken);
        if (mirror is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        mirror.IsEnabled = !paused;
        mirror.Status = paused ? RemoteMirrorStatus.Paused : RemoteMirrorStatus.Pending;
        mirror.LastErrorCode = null;
        mirror.UpdatedAtUtc = now.UtcDateTime;
        AddAudit(dbContext, mirror, actorUserId, paused ? "mirror.pause" : "mirror.resume", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (paused && mirror.Job is not null)
        {
            _ = await _jobQueue.RequestCancellationAsync(mirror.Job.Id, cancellationToken);
            _dispatcher.CancelActive(mirror.Job.Id);
        }
        else if (!paused)
        {
            await _jobQueue.EnqueueAsync(mirror.Id, RemoteMirrorJobTrigger.Manual, cancellationToken: cancellationToken);
        }
        return true;
    }

    public async Task<bool> EnqueueAsync(
        long repositoryId,
        long mirrorId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var mirror = await dbContext.RepositoryMirrors.SingleOrDefaultAsync(
            item => item.Id == mirrorId && item.RepositoryId == repositoryId && item.IsEnabled,
            cancellationToken);
        if (mirror is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        mirror.Status = RemoteMirrorStatus.Pending;
        mirror.LastErrorCode = null;
        mirror.UpdatedAtUtc = now.UtcDateTime;
        AddAudit(dbContext, mirror, actorUserId, "mirror.enqueue", now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await _jobQueue.EnqueueAsync(mirror.Id, RemoteMirrorJobTrigger.Manual, cancellationToken: cancellationToken);
        return true;
    }

    public async Task<bool> CancelAsync(
        long repositoryId,
        long jobId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        var mirror = await FindMirrorForJobAsync(repositoryId, jobId, cancellationToken);
        if (mirror is null || !await _jobQueue.RequestCancellationAsync(jobId, cancellationToken))
        {
            return false;
        }
        _dispatcher.CancelActive(jobId);
        await AddAuditAsync(mirror, actorUserId, "mirror.job.cancel", cancellationToken);
        return true;
    }

    public async Task<bool> RetryAsync(
        long repositoryId,
        long jobId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        var mirror = await FindMirrorForJobAsync(repositoryId, jobId, cancellationToken);
        if (mirror is null || !await _jobQueue.RetryAsync(jobId, cancellationToken))
        {
            return false;
        }
        await AddAuditAsync(mirror, actorUserId, "mirror.job.retry", cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(
        long repositoryId,
        long mirrorId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var mirror = await dbContext.RepositoryMirrors
            .Include(item => item.Job)
            .SingleOrDefaultAsync(item => item.Id == mirrorId && item.RepositoryId == repositoryId, cancellationToken);
        if (mirror is null || mirror.Job?.State == RemoteMirrorJobState.Leased)
        {
            return false;
        }
        AddAudit(dbContext, mirror, actorUserId, "mirror.delete", _timeProvider.GetUtcNow());
        dbContext.RepositoryMirrors.Remove(mirror);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<GitCandyRepositoryMirror?> FindMirrorForJobAsync(
        long repositoryId,
        long jobId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.RemoteMirrorJobs.AsNoTracking()
            .Where(item => item.Id == jobId
                && item.Mirror != null
                && item.Mirror.RepositoryId == repositoryId)
            .Select(item => item.Mirror)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task AddAuditAsync(
        GitCandyRepositoryMirror mirror,
        string actorUserId,
        string action,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        AddAudit(dbContext, mirror, actorUserId, action, _timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void AddAudit(
        GitCandyDbContext dbContext,
        GitCandyRepositoryMirror mirror,
        string actorUserId,
        string action,
        DateTimeOffset occurredAt)
    {
        dbContext.GovernanceAuditEvents.Add(new GitCandyGovernanceAuditEvent
        {
            RepositoryId = mirror.RepositoryId,
            ActorUserId = actorUserId,
            Action = action,
            Outcome = "success",
            ReferenceName = string.Empty,
            Detail = $"mirror={mirror.Id}",
            OccurredAtUtc = occurredAt.UtcDateTime
        });
    }

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
