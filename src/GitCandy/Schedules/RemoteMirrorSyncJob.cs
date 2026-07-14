using GitCandy.Remotes;

namespace GitCandy.Schedules;

/// <summary>唤醒到期 Pull mirror 和成功 push 产生的 pending ref 事件。</summary>
public sealed class RemoteMirrorSyncJob(
    IRemoteMirrorService mirrorService,
    ILogger<RemoteMirrorSyncJob> logger) : ISchedulerJob
{
    private const int BatchSize = 10;
    private readonly IRemoteMirrorService _mirrorService = mirrorService;
    private readonly ILogger<RemoteMirrorSyncJob> _logger = logger;

    public string Name => "remote-mirror-sync";

    public SchedulerJobType JobType => SchedulerJobType.LongRunning;

    public async ValueTask ExecuteAsync(
        SchedulerJobContext context,
        CancellationToken cancellationToken = default)
    {
        var pushes = await _mirrorService.ProcessPendingPushMirrorsAsync(BatchSize, cancellationToken);
        var pulls = await _mirrorService.SynchronizeDuePullMirrorsAsync(BatchSize, cancellationToken);
        foreach (var result in pushes.Concat(pulls).Where(item => !item.Succeeded))
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
