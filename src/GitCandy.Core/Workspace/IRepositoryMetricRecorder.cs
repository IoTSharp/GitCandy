namespace GitCandy.Workspace;

/// <summary>公开推荐信号和私人近期交互的有界采集入口。</summary>
public interface IRepositoryMetricRecorder
{
    Task RecordPageViewAsync(long repositoryId, string visitorKey, string? userId, CancellationToken cancellationToken = default);
    Task RecordSuccessfulDownloadAsync(long repositoryId, CancellationToken cancellationToken = default);
    Task RecordSuccessfulGitFetchAsync(long repositoryId, string? userId, CancellationToken cancellationToken = default);
    Task ReplaceCommitActivityAsync(long repositoryId, IReadOnlyDictionary<DateOnly, int> commitsByDay, CancellationToken cancellationToken = default);
    Task SetLicenseAsync(long repositoryId, string? licenseSpdx, CancellationToken cancellationToken = default);
}
