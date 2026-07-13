using GitCandy.Application;
using GitCandy.Git;
using GitCandy.Workspace;

namespace GitCandy.Schedules;

/// <summary>增量生成活动、公开指标和不可变推荐快照。</summary>
public sealed class WorkspaceProjectionJob(
    IWorkspaceService workspaceService,
    IRepositoryService repositoryService,
    IRepositoryMetricRecorder metricRecorder,
    IRepositoryBrowserService repositoryBrowserService,
    IGitServiceFactory gitServiceFactory,
    ILogger<WorkspaceProjectionJob> logger) : ISchedulerJob
{
    private readonly IWorkspaceService _workspaceService = workspaceService;
    private readonly IRepositoryService _repositoryService = repositoryService;
    private readonly IRepositoryMetricRecorder _metricRecorder = metricRecorder;
    private readonly IRepositoryBrowserService _repositoryBrowserService = repositoryBrowserService;
    private readonly IGitServiceFactory _gitServiceFactory = gitServiceFactory;
    private readonly ILogger<WorkspaceProjectionJob> _logger = logger;

    public string Name => "workspace-projections";

    public SchedulerJobType JobType => SchedulerJobType.LongRunning;

    public async ValueTask ExecuteAsync(SchedulerJobContext context, CancellationToken cancellationToken = default)
    {
        var repositories = await _repositoryService.GetVisibleRepositoriesAsync(null, false, cancellationToken);
        foreach (var repository in repositories)
        {
            try
            {
                var git = _gitServiceFactory.Create(repository.StorageName);
                var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
                var commits = new List<RepositoryCommitSummary>();
                for (var page = 1; page <= 10; page++)
                {
                    var result = _repositoryBrowserService.ReadCommits(git, null, page, 100, cancellationToken);
                    if (result is null) break;
                    commits.AddRange(result.Commits.Where(item => item.AuthoredAt >= cutoff));
                    if (!result.HasNextPage || result.Commits.Count == 0 || result.Commits[^1].AuthoredAt < cutoff) break;
                }
                var daily = commits.GroupBy(item => DateOnly.FromDateTime(item.AuthoredAt.UtcDateTime))
                    .ToDictionary(group => group.Key, group => group.Count());
                await _metricRecorder.ReplaceCommitActivityAsync(repository.Id, daily, cancellationToken);
                var tree = _repositoryBrowserService.ReadTree(git, null, null, cancellationToken);
                var license = tree?.Entries.FirstOrDefault(item => item.Kind == RepositoryTreeEntryKind.Blob
                    && (string.Equals(item.Name, "LICENSE", StringComparison.OrdinalIgnoreCase)
                        || item.Name.StartsWith("LICENSE.", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(item.Name, "COPYING", StringComparison.OrdinalIgnoreCase)));
                string? spdx = null;
                if (license is not null)
                {
                    var blob = _repositoryBrowserService.ReadBlob(git, tree!.Revision.CommitId, license.Path, cancellationToken);
                    spdx = TryReadSpdxIdentifier(blob?.Text);
                }
                await _metricRecorder.SetLicenseAsync(repository.Id, spdx, cancellationToken);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or GitRepositoryNotFoundException or LibGit2Sharp.LibGit2SharpException)
            {
                _logger.LogWarning(exception, "Workspace metrics skipped repository {RepositoryId}.", repository.Id);
            }
        }
        await _workspaceService.RefreshProjectionsAsync(cancellationToken);
    }

    public TimeSpan GetNextInterval(SchedulerJobContext context) => TimeSpan.FromMinutes(5);

    private static string? TryReadSpdxIdentifier(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        const string marker = "SPDX-License-Identifier:";
        foreach (var line in text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var value = line[(index + marker.Length)..].Trim().TrimEnd('*', '/', '-', ' ');
            return value.Length is > 0 and <= 64 ? value : null;
        }
        return null;
    }
}
