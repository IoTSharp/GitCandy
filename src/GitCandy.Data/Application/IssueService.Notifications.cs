using System.Text.RegularExpressions;
using GitCandy.Data.Domain;
using GitCandy.Issues;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed partial class IssueService
{
    public async Task<IReadOnlyList<IssueNotificationSummary>> GetNotificationsAsync(string userId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var rows = await (from notification in _dbContext.IssueNotifications.AsNoTracking()
            join issue in _dbContext.Issues.AsNoTracking() on notification.IssueId equals issue.Id
            join repository in _dbContext.Repositories.AsNoTracking() on notification.RepositoryId equals repository.Id
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository.NamespaceId equals repositoryNamespace.Id
            join actor in _dbContext.Users.AsNoTracking() on notification.ActorUserId equals actor.Id into actors
            from actor in actors.DefaultIfEmpty()
            where notification.UserId == userId
            orderby notification.CreatedAtUtc descending
            select new
            {
                Notification = notification,
                Issue = issue,
                Repository = repository,
                NamespaceSlug = repositoryNamespace.Slug,
                Actor = actor == null ? null : actor.UserName
            }).Take(100).ToArrayAsync(cancellationToken);
        var visible = new List<IssueNotificationSummary>(rows.Length);
        foreach (var row in rows)
        {
            if (!await _permissionQuery.CanReadRepositoryAsync(row.Repository.Id, userId, isAdministrator, cancellationToken))
                continue;
            visible.Add(new IssueNotificationSummary(row.Notification.Id, row.Notification.Type, row.NamespaceSlug,
                row.Repository.Name, row.Issue.Number, row.Issue.Title, row.Actor, row.Notification.CreatedAtUtc, row.Notification.ReadAtUtc));
        }
        return visible;
    }

    public async Task MarkNotificationReadAsync(long notificationId, string userId, CancellationToken cancellationToken = default)
    {
        var notification = await _dbContext.IssueNotifications.SingleOrDefaultAsync(item => item.Id == notificationId && item.UserId == userId, cancellationToken);
        if (notification is null || !await _permissionQuery.CanReadRepositoryAsync(notification.RepositoryId, userId, false, cancellationToken)) return;
        notification.ReadAtUtc ??= UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ApplyClosingReferencesAsync(long repositoryId, string actorUserId, string text, string source, CancellationToken cancellationToken = default)
    {
        var numbers = ClosingReferenceRegex().Matches(text).Cast<Match>().Select(match => long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToArray();
        if (numbers.Length == 0) return 0;
        var issues = await _dbContext.Issues.Include(item => item.Timeline).Where(item => item.RepositoryId == repositoryId && numbers.Contains(item.Number) && item.State == IssueState.Open).ToArrayAsync(cancellationToken);
        foreach (var issue in issues)
        {
            issue.State = IssueState.Closed;
            issue.ClosedAtUtc = UtcNow;
            Touch(issue);
            issue.Timeline.Add(NewEvent(IssueEventType.AutoClosed, actorUserId, UtcNow, source.Length > 200 ? source[..200] : source));
            await NotifyParticipantsAsync(issue, actorUserId, IssueNotificationType.Status, [], cancellationToken);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return issues.Length;
    }

    private async Task AddReferencesAndNotificationsAsync(GitCandyIssue sourceIssue, string actorUserId, string body, IssueNotificationType notificationType, CancellationToken cancellationToken)
    {
        var mentionedUsers = await ResolveMentionedUsersAsync(body, sourceIssue.RepositoryId, actorUserId, cancellationToken);
        foreach (var match in LocalIssueReferenceRegex().Matches(body).Cast<Match>())
        {
            var number = long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var target = await _dbContext.Issues.SingleOrDefaultAsync(item => item.RepositoryId == sourceIssue.RepositoryId && item.Number == number, cancellationToken);
            if (target is null) continue;
            _dbContext.IssueReferences.Add(new GitCandyIssueReference
            {
                SourceIssueId = sourceIssue.Id,
                TargetRepositoryId = sourceIssue.RepositoryId,
                TargetIssueId = target.Id,
                DisplayText = match.Value,
                CreatedAtUtc = UtcNow
            });
            if (target.Id != sourceIssue.Id)
            {
                _dbContext.IssueTimelineEvents.Add(new GitCandyIssueTimelineEvent
                {
                    IssueId = target.Id,
                    ActorUserId = actorUserId,
                    Type = IssueEventType.Related,
                    Detail = $"Referenced by #{sourceIssue.Number}",
                    CreatedAtUtc = UtcNow
                });
            }
        }
        foreach (var match in CommitReferenceRegex().Matches(body).Cast<Match>())
        {
            _dbContext.IssueReferences.Add(new GitCandyIssueReference
            {
                SourceIssueId = sourceIssue.Id,
                TargetRepositoryId = sourceIssue.RepositoryId,
                CommitSha = match.Value,
                DisplayText = match.Value,
                CreatedAtUtc = UtcNow
            });
        }
        await AddCrossRepositoryReferencesAsync(sourceIssue, actorUserId, body, cancellationToken);
        await NotifyParticipantsAsync(sourceIssue, actorUserId, notificationType, mentionedUsers, cancellationToken);
    }

    private async Task AddCrossRepositoryReferencesAsync(GitCandyIssue sourceIssue, string actorUserId, string body, CancellationToken cancellationToken)
    {
        foreach (var match in CrossRepositoryIssueReferenceRegex().Matches(body).Cast<Match>())
        {
            var namespaceName = match.Groups[1].Value.ToUpperInvariant();
            var repositoryName = match.Groups[2].Value.ToUpperInvariant();
            var number = long.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            var target = await (from issue in _dbContext.Issues
                join repository in _dbContext.Repositories on issue.RepositoryId equals repository.Id
                join repositoryNamespace in _dbContext.Namespaces on repository.NamespaceId equals repositoryNamespace.Id
                where repositoryNamespace.NormalizedSlug == namespaceName && repository.NormalizedName == repositoryName && issue.Number == number
                select new { Issue = issue, Repository = repository }).SingleOrDefaultAsync(cancellationToken);
            if (target is null || !await _permissionQuery.CanReadRepositoryAsync(target.Repository.Id, actorUserId, await IsAdministratorAsync(actorUserId, cancellationToken), cancellationToken)) continue;
            _dbContext.IssueReferences.Add(new GitCandyIssueReference
            {
                SourceIssueId = sourceIssue.Id,
                TargetRepositoryId = target.Repository.Id,
                TargetIssueId = target.Issue.Id,
                DisplayText = match.Value,
                CreatedAtUtc = UtcNow
            });
            _dbContext.IssueTimelineEvents.Add(new GitCandyIssueTimelineEvent
            {
                IssueId = target.Issue.Id,
                ActorUserId = actorUserId,
                Type = IssueEventType.Related,
                Detail = "Referenced from another readable repository.",
                CreatedAtUtc = UtcNow
            });
        }
    }

    private async Task<IReadOnlyCollection<string>> ResolveMentionedUsersAsync(string body, long repositoryId, string actorUserId, CancellationToken cancellationToken)
    {
        var names = MentionRegex().Matches(body).Cast<Match>().Select(match => match.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
        if (names.Length == 0) return [];
        var users = await _dbContext.Users.AsNoTracking().Where(item => item.NormalizedUserName != null && names.Contains(item.NormalizedUserName)).Select(item => item.Id).ToArrayAsync(cancellationToken);
        var visible = new List<string>(users.Length);
        foreach (var userId in users)
        {
            if (await _permissionQuery.CanReadRepositoryAsync(repositoryId, userId, isAdministrator: false, cancellationToken)) visible.Add(userId);
        }
        return visible;
    }

    private async Task NotifyParticipantsAsync(GitCandyIssue issue, string actorUserId, IssueNotificationType type, IReadOnlyCollection<string> additionalRecipients, CancellationToken cancellationToken)
    {
        var recipients = issue.Subscriptions.Where(item => item.IsSubscribed).Select(item => item.UserId)
            .Append(issue.AuthorUserId).Concat(additionalRecipients);
        if (issue.AssigneeUserId is not null) recipients = recipients.Append(issue.AssigneeUserId);
        foreach (var userId in recipients.Where(item => item != actorUserId).Distinct(StringComparer.Ordinal))
        {
            if (!await _permissionQuery.CanReadRepositoryAsync(issue.RepositoryId, userId, false, cancellationToken)) continue;
            _dbContext.IssueNotifications.Add(new GitCandyIssueNotification
            {
                UserId = userId,
                RepositoryId = issue.RepositoryId,
                IssueId = issue.Id,
                ActorUserId = actorUserId,
                Type = type,
                CreatedAtUtc = UtcNow
            });
        }
    }

    private Task<bool> IsAdministratorAsync(string userId, CancellationToken cancellationToken) =>
        (from userRole in _dbContext.UserRoles.AsNoTracking()
         join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
         where userRole.UserId == userId && role.NormalizedName == "ADMINISTRATOR"
         select userRole).AnyAsync(cancellationToken);

    [GeneratedRegex(@"(?i)\b(?:fix(?:e[sd])?|close[sd]?|resolve[sd]?)\s+#([1-9][0-9]*)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ClosingReferenceRegex();
    [GeneratedRegex(@"(?<![\w/])#([1-9][0-9]*)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LocalIssueReferenceRegex();
    [GeneratedRegex(@"(?<![\w/])([A-Za-z0-9][A-Za-z0-9_.-]{0,49})/([A-Za-z0-9][A-Za-z0-9_.-]{0,49})#([1-9][0-9]*)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CrossRepositoryIssueReferenceRegex();
    [GeneratedRegex(@"(?<![0-9a-fA-F])[0-9a-fA-F]{7,40}(?![0-9a-fA-F])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CommitReferenceRegex();
}
