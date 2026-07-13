using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Workspace;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class RepositoryMetricRecorder(GitCandyDbContext dbContext, TimeProvider timeProvider) : IRepositoryMetricRecorder
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly TimeProvider _timeProvider = timeProvider;
    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task RecordPageViewAsync(long repositoryId, string visitorKey, string? userId, CancellationToken cancellationToken = default)
    {
        if (visitorKey.Length is < 16 or > 64) return;
        var day = UtcNow.Date;
        if (!await _dbContext.RepositoryPageViews.AnyAsync(item => item.RepositoryId == repositoryId && item.DayUtc == day && item.VisitorKey == visitorKey, cancellationToken))
        {
            _dbContext.RepositoryPageViews.Add(new GitCandyRepositoryPageView { RepositoryId = repositoryId, DayUtc = day, VisitorKey = visitorKey });
            var metric = await GetOrCreateMetricAsync(repositoryId, day, cancellationToken);
            metric.UniquePageViewCount++;
        }
        await RecordInteractionAsync(repositoryId, userId, cancellationToken);
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { _dbContext.ChangeTracker.Clear(); }
    }

    public Task RecordSuccessfulDownloadAsync(long repositoryId, CancellationToken cancellationToken = default) =>
        IncrementAsync(repositoryId, download: true, cancellationToken);

    public async Task RecordSuccessfulGitFetchAsync(long repositoryId, string? userId, CancellationToken cancellationToken = default)
    {
        var metric = await GetOrCreateMetricAsync(repositoryId, UtcNow.Date, cancellationToken);
        metric.SuccessfulGitFetchCount++;
        await RecordInteractionAsync(repositoryId, userId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceCommitActivityAsync(long repositoryId, IReadOnlyDictionary<DateOnly, int> commitsByDay, CancellationToken cancellationToken = default)
    {
        foreach (var pair in commitsByDay)
        {
            var day = pair.Key.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var metric = await GetOrCreateMetricAsync(repositoryId, day, cancellationToken);
            metric.CommitCount = Math.Max(0, pair.Value);
            metric.ActiveCommitDays = pair.Value > 0 ? 1 : 0;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetLicenseAsync(long repositoryId, string? licenseSpdx, CancellationToken cancellationToken = default)
    {
        var normalized = string.IsNullOrWhiteSpace(licenseSpdx) ? null : licenseSpdx.Trim();
        if (normalized?.Length > 64) return;
        var metric = await GetOrCreateMetricAsync(repositoryId, UtcNow.Date, cancellationToken);
        metric.LicenseSpdx = normalized;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task IncrementAsync(long repositoryId, bool download, CancellationToken cancellationToken)
    {
        var metric = await GetOrCreateMetricAsync(repositoryId, UtcNow.Date, cancellationToken);
        if (download) metric.SuccessfulDownloadCount++;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordInteractionAsync(long repositoryId, string? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        var interaction = await _dbContext.RepositoryInteractions.SingleOrDefaultAsync(item => item.RepositoryId == repositoryId && item.UserId == userId, cancellationToken);
        if (interaction is null) _dbContext.RepositoryInteractions.Add(new GitCandyRepositoryInteraction
            { RepositoryId = repositoryId, UserId = userId, LastInteractedAtUtc = UtcNow, InteractionCount = 1 });
        else { interaction.LastInteractedAtUtc = UtcNow; interaction.InteractionCount++; }
    }

    private async Task<GitCandyRepositoryMetricDaily> GetOrCreateMetricAsync(long repositoryId, DateTime day, CancellationToken cancellationToken)
    {
        var metric = await _dbContext.RepositoryMetricsDaily.SingleOrDefaultAsync(item => item.RepositoryId == repositoryId && item.DayUtc == day, cancellationToken);
        if (metric is not null) return metric;
        metric = new GitCandyRepositoryMetricDaily { RepositoryId = repositoryId, DayUtc = day };
        _dbContext.RepositoryMetricsDaily.Add(metric);
        return metric;
    }
}
