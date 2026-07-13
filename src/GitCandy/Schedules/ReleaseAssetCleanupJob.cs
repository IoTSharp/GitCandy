using GitCandy.Releases;

namespace GitCandy.Schedules;

/// <summary>清理数据库提交失败或中断上传遗留的 Release 附件。</summary>
public sealed class ReleaseAssetCleanupJob(
    IReleaseService releaseService,
    ILogger<ReleaseAssetCleanupJob> logger) : ISchedulerJob
{
    public string Name => "release-asset-cleanup";
    public SchedulerJobType JobType => SchedulerJobType.LongRunning;

    public async ValueTask ExecuteAsync(SchedulerJobContext context, CancellationToken cancellationToken = default)
    {
        var deleted = await releaseService.CleanupOrphansAsync(cancellationToken);
        if (deleted > 0)
        {
            logger.LogInformation("Deleted {DeletedCount} orphaned Release asset files.", deleted);
        }
    }

    public TimeSpan GetNextInterval(SchedulerJobContext context) => TimeSpan.FromHours(1);
}
