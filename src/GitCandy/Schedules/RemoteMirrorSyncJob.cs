using GitCandy.Remotes;

namespace GitCandy.Schedules;

/// <summary>唤醒到期 Pull mirror 和成功 push 产生的 pending ref 事件。</summary>
public sealed class RemoteMirrorSyncJob(
    IRemoteMirrorJobQueue queue,
    IRemoteMirrorJobDispatcher dispatcher,
    ILogger<RemoteMirrorSyncJob> logger) : ISchedulerJob
{
    private const int BatchSize = 10;
    private readonly IRemoteMirrorJobQueue _queue = queue;
    private readonly IRemoteMirrorJobDispatcher _dispatcher = dispatcher;
    private readonly ILogger<RemoteMirrorSyncJob> _logger = logger;

    public string Name => "remote-mirror-sync";

    public SchedulerJobType JobType => SchedulerJobType.LongRunning;

    public async ValueTask ExecuteAsync(
        SchedulerJobContext context,
        CancellationToken cancellationToken = default)
    {
        _ = await _queue.EnqueueRecoveryCandidatesAsync(BatchSize, cancellationToken);
        _ = await _queue.EnqueueDuePullMirrorsAsync(BatchSize, cancellationToken);
        var results = await _dispatcher.RunReadyAsync(cancellationToken);
        foreach (var result in results.Where(item => !item.Succeeded))
        {
            _logger.LogWarning(
                "Remote mirror {MirrorId} completed with status {Status} and error {ErrorCode}.",
                result.MirrorId,
                result.Status,
                result.ErrorCode);
        }
    }

    public TimeSpan GetNextInterval(SchedulerJobContext context) => TimeSpan.FromSeconds(5);
}
