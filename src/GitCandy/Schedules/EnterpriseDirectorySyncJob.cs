using GitCandy.Enterprise;

namespace GitCandy.Schedules;

/// <summary>按连接隔离失败的企业目录同步与停用对账作业。</summary>
public sealed class EnterpriseDirectorySyncJob(
    IEnterpriseDirectorySyncService syncService,
    ILogger<EnterpriseDirectorySyncJob> logger) : ISchedulerJob
{
    private readonly IEnterpriseDirectorySyncService _syncService = syncService;
    private readonly ILogger<EnterpriseDirectorySyncJob> _logger = logger;

    public string Name => "enterprise-directory-sync";
    public SchedulerJobType JobType => SchedulerJobType.LongRunning;

    public async ValueTask ExecuteAsync(
        SchedulerJobContext context,
        CancellationToken cancellationToken = default)
    {
        var results = await _syncService.SynchronizeAllAsync(cancellationToken);
        foreach (var result in results.Where(item => !item.Succeeded))
        {
            _logger.LogWarning(
                "Enterprise directory connection {ConnectionId} failed with {ErrorCode}.",
                result.ConnectionId,
                result.ErrorCode);
        }
    }

    public TimeSpan GetNextInterval(SchedulerJobContext context) => TimeSpan.FromMinutes(15);
}
