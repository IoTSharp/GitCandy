using System.Data;
using System.Text.RegularExpressions;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Permissions;
using GitCandy.Issues;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>基于 EF Core 的 Issue 闭环应用服务。</summary>
internal sealed partial class IssueService(
    GitCandyDbContext dbContext,
    IIssueMarkdownRenderer markdownRenderer,
    IGitCandyRepositoryPermissionQuery permissionQuery,
    TimeProvider timeProvider) : IIssueService
{
    private const int MaxDiscussionActionsPerMinute = 20;
    private const int MaxMentionFanOut = 25;
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly IIssueMarkdownRenderer _markdownRenderer = markdownRenderer;
    private readonly IGitCandyRepositoryPermissionQuery _permissionQuery = permissionQuery;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<IssuePage> GetIssuesAsync(
        long repositoryId,
        IssueQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var issues = _dbContext.Issues.AsNoTracking().Where(item =>
            item.RepositoryId == repositoryId && item.State == query.State);
        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            var normalized = query.Author.Trim().ToUpperInvariant();
            issues = issues.Where(item => item.Author!.NormalizedUserName == normalized);
        }
        if (!string.IsNullOrWhiteSpace(query.Assignee))
        {
            var normalized = query.Assignee.Trim().ToUpperInvariant();
            issues = issues.Where(item => item.Assignee != null && item.Assignee.NormalizedUserName == normalized);
        }
        if (!string.IsNullOrWhiteSpace(query.Label))
        {
            var normalized = query.Label.Trim().ToUpperInvariant();
            issues = issues.Where(item => item.LabelLinks.Any(link => link.Label!.NormalizedName == normalized));
        }
        if (query.MilestoneId is not null)
        {
            issues = issues.Where(item => item.MilestoneId == query.MilestoneId);
        }

        var totalCount = await issues.CountAsync(cancellationToken);
        var entities = await issues
            .Include(item => item.Author)
            .Include(item => item.Assignee)
            .Include(item => item.Milestone)
            .Include(item => item.LabelLinks).ThenInclude(link => link.Label)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenByDescending(item => item.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var issueIds = entities.Select(static item => item.Id).ToArray();
        var commentCounts = await _dbContext.IssueComments.AsNoTracking()
            .Where(item => issueIds.Contains(item.IssueId) && !item.IsHidden)
            .GroupBy(item => item.IssueId)
            .Select(group => new { IssueId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.IssueId, item => item.Count, cancellationToken);

        return new IssuePage(page, pageSize, totalCount, entities.Select(item => new IssueSummary(
            item.Id,
            item.Number,
            item.Title,
            item.State,
            item.Author?.UserName ?? string.Empty,
            item.Assignee?.UserName,
            item.CreatedAtUtc,
            item.UpdatedAtUtc,
            commentCounts.GetValueOrDefault(item.Id),
            item.LabelLinks.Where(link => link.Label is not null).Select(link => ToSummary(link.Label!)).ToArray(),
            item.Milestone?.Title)).ToArray());
    }

    public async Task<IssueDetails?> GetIssueAsync(
        long repositoryId,
        long number,
        string? viewerUserId,
        CancellationToken cancellationToken = default)
    {
        var issue = await _dbContext.Issues.AsNoTracking()
            .Include(item => item.Author)
            .Include(item => item.Assignee)
            .Include(item => item.Milestone)
            .Include(item => item.LabelLinks).ThenInclude(link => link.Label)
            .Include(item => item.Subscriptions)
            .SingleOrDefaultAsync(item => item.RepositoryId == repositoryId && item.Number == number, cancellationToken);
        if (issue is null)
        {
            return null;
        }

        var events = await _dbContext.IssueTimelineEvents.AsNoTracking()
            .Include(item => item.Actor)
            .Include(item => item.Comment).ThenInclude(item => item!.Author)
            .Where(item => item.IssueId == issue.Id)
            .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var timeline = events.Select(item => new IssueTimelineItem(
            item.Id,
            item.CommentId,
            item.Type,
            item.Actor?.UserName,
            item.Comment is null || item.Comment.IsHidden ? null : item.Comment.BodyHtml,
            item.Comment is null || item.Comment.IsHidden ? null : item.Comment.BodyMarkdown,
            item.Comment?.IsHidden == true ? "Comment hidden by a repository owner." : item.Detail,
            item.CreatedAtUtc,
            item.Comment?.EditedAtUtc,
            item.Comment?.IsHidden == true,
            item.Comment is not null && item.Comment.AuthorUserId == viewerUserId)).ToArray();
        return new IssueDetails(
            issue.Id,
            issue.RepositoryId,
            issue.Number,
            issue.Title,
            issue.BodyMarkdown,
            issue.BodyHtml,
            issue.State,
            issue.AuthorUserId,
            issue.Author?.UserName ?? string.Empty,
            issue.AssigneeUserId,
            issue.Assignee?.UserName,
            issue.MilestoneId,
            issue.Milestone?.Title,
            issue.IsLocked,
            issue.CreatedAtUtc,
            issue.UpdatedAtUtc,
            issue.ClosedAtUtc,
            issue.Version,
            issue.Subscriptions.Any(item => item.UserId == viewerUserId && item.IsSubscribed),
            issue.LabelLinks.Where(link => link.Label is not null).Select(link => ToSummary(link.Label!)).ToArray(),
            timeline);
    }

    public async Task<IssueDetails> CreateIssueAsync(
        long repositoryId,
        CreateIssueCommand command,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CreateIssueAttemptAsync(repositoryId, command, cancellationToken);
            }
            catch (Exception exception) when (attempt < 10 && IsTransientDiscussionConflict(exception))
            {
                _dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }
    }

    private async Task<IssueDetails> CreateIssueAttemptAsync(
        long repositoryId,
        CreateIssueCommand command,
        CancellationToken cancellationToken)
    {
        ValidateTitleAndBody(command.Title, command.Body);
        if (!await CanReadAsUserAsync(repositoryId, command.AuthorUserId, cancellationToken))
            throw new IssueValidationException("The repository cannot be accessed.");
        var address = await GetAddressAsync(repositoryId, cancellationToken)
            ?? throw new IssueValidationException("The repository does not exist.");
        ValidateMentionFanOut(command.Body);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await EnforceRateLimitAsync(command.AuthorUserId, cancellationToken);
        var sequence = await _dbContext.WorkItemSequences.SingleOrDefaultAsync(
            item => item.RepositoryId == repositoryId, cancellationToken);
        if (sequence is null)
        {
            sequence = new GitCandyWorkItemSequence { RepositoryId = repositoryId };
            _dbContext.WorkItemSequences.Add(sequence);
        }

        var now = UtcNow;
        var issue = new GitCandyIssue
        {
            RepositoryId = repositoryId,
            Number = sequence.NextNumber,
            Title = command.Title.Trim(),
            BodyMarkdown = command.Body.Trim(),
            BodyHtml = _markdownRenderer.Render(command.Body, address.NamespaceSlug, address.RepositorySlug),
            AuthorUserId = command.AuthorUserId,
            State = IssueState.Open,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 1
        };
        sequence.NextNumber++;
        sequence.Version++;
        await ApplyInitialMetadataAsync(issue, command, cancellationToken);
        issue.Subscriptions.Add(new GitCandyIssueSubscription
        {
            UserId = command.AuthorUserId,
            IsSubscribed = true,
            UpdatedAtUtc = now
        });
        issue.Timeline.Add(NewEvent(IssueEventType.Created, command.AuthorUserId, now));
        _dbContext.Issues.Add(issue);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await AddReferencesAndNotificationsAsync(issue, command.AuthorUserId, command.Body, IssueNotificationType.Mention, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetIssueAsync(repositoryId, issue.Number, command.AuthorUserId, cancellationToken))!;
    }

    public async Task<IssueMutationResult> EditIssueAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        EditIssueCommand command,
        CancellationToken cancellationToken = default)
    {
        try { ValidateTitleAndBody(command.Title, command.Body); ValidateMentionFanOut(command.Body); }
        catch (IssueValidationException) { return IssueMutationResult.Invalid; }
        var issue = await FindIssueAsync(repositoryId, number, cancellationToken);
        if (issue is null) return IssueMutationResult.NotFound;
        if (!await CanReadAsUserAsync(repositoryId, actorUserId, cancellationToken)) return IssueMutationResult.Forbidden;
        if (!isOwner && issue.AuthorUserId != actorUserId) return IssueMutationResult.Forbidden;
        if (issue.Version != command.Version) return IssueMutationResult.Conflict;
        var address = await GetAddressAsync(repositoryId, cancellationToken);
        if (address is null) return IssueMutationResult.NotFound;
        _dbContext.IssueEdits.Add(new GitCandyIssueEdit
        {
            IssueId = issue.Id,
            EditorUserId = actorUserId,
            PreviousMarkdown = issue.BodyMarkdown,
            PreviousHtml = issue.BodyHtml,
            EditedAtUtc = UtcNow
        });
        issue.Title = command.Title.Trim();
        issue.BodyMarkdown = command.Body.Trim();
        issue.BodyHtml = _markdownRenderer.Render(command.Body, address.Value.NamespaceSlug, address.Value.RepositorySlug);
        Touch(issue);
        issue.Timeline.Add(NewEvent(IssueEventType.Edited, actorUserId, UtcNow));
        await AddReferencesAndNotificationsAsync(issue, actorUserId, command.Body, IssueNotificationType.Mention, cancellationToken);
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<IssueMutationResult> SetStateAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        IssueState state,
        CancellationToken cancellationToken = default)
    {
        var issue = await FindIssueAsync(repositoryId, number, cancellationToken);
        if (issue is null) return IssueMutationResult.NotFound;
        if (!await CanReadAsUserAsync(repositoryId, actorUserId, cancellationToken)) return IssueMutationResult.Forbidden;
        if (!isOwner && issue.AuthorUserId != actorUserId && issue.AssigneeUserId != actorUserId) return IssueMutationResult.Forbidden;
        if (issue.State == state) return IssueMutationResult.Succeeded;
        issue.State = state;
        issue.ClosedAtUtc = state == IssueState.Closed ? UtcNow : null;
        Touch(issue);
        issue.Timeline.Add(NewEvent(state == IssueState.Closed ? IssueEventType.Closed : IssueEventType.Reopened, actorUserId, UtcNow));
        await NotifyParticipantsAsync(issue, actorUserId, IssueNotificationType.Status, [], cancellationToken);
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<IssueMutationResult> AddCommentAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > SchemaLimits.IssueBody) return IssueMutationResult.Invalid;
        try { ValidateMentionFanOut(body); }
        catch (IssueValidationException) { return IssueMutationResult.Invalid; }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AddCommentAttemptAsync(
                    repositoryId,
                    number,
                    actorUserId,
                    isOwner,
                    body,
                    cancellationToken);
            }
            catch (IssueRateLimitException)
            {
                return IssueMutationResult.RateLimited;
            }
            catch (Exception exception) when (attempt < 10 && IsTransientDiscussionConflict(exception))
            {
                _dbContext.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken);
            }
        }
    }

    private async Task<IssueMutationResult> AddCommentAttemptAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        string body,
        CancellationToken cancellationToken)
    {
        var issue = await FindIssueAsync(repositoryId, number, cancellationToken);
        if (issue is null) return IssueMutationResult.NotFound;
        if (!await CanReadAsUserAsync(repositoryId, actorUserId, cancellationToken)) return IssueMutationResult.Forbidden;
        if (issue.IsLocked && !isOwner) return IssueMutationResult.Locked;
        var address = await GetAddressAsync(repositoryId, cancellationToken);
        if (address is null) return IssueMutationResult.NotFound;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        await EnforceRateLimitAsync(actorUserId, cancellationToken);
        var now = UtcNow;
        var comment = new GitCandyIssueComment
        {
            AuthorUserId = actorUserId,
            BodyMarkdown = body.Trim(),
            BodyHtml = _markdownRenderer.Render(body, address.Value.NamespaceSlug, address.Value.RepositorySlug),
            CreatedAtUtc = now,
            Version = 1
        };
        issue.Comments.Add(comment);
        issue.Timeline.Add(NewEvent(IssueEventType.Commented, actorUserId, now, comment: comment));
        Touch(issue);
        await AddReferencesAndNotificationsAsync(issue, actorUserId, body, IssueNotificationType.Reply, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IssueMutationResult.Succeeded;
    }

    public async Task<IssueMutationResult> EditCommentAsync(
        long repositoryId,
        long number,
        long commentId,
        string actorUserId,
        bool isOwner,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > SchemaLimits.IssueBody) return IssueMutationResult.Invalid;
        var issue = await FindIssueAsync(repositoryId, number, cancellationToken);
        if (issue is null) return IssueMutationResult.NotFound;
        if (!await CanReadAsUserAsync(repositoryId, actorUserId, cancellationToken)) return IssueMutationResult.Forbidden;
        var comment = await _dbContext.IssueComments.SingleOrDefaultAsync(item => item.Id == commentId && item.IssueId == issue.Id, cancellationToken);
        if (comment is null) return IssueMutationResult.NotFound;
        if (!isOwner && comment.AuthorUserId != actorUserId) return IssueMutationResult.Forbidden;
        if (comment.IsHidden) return IssueMutationResult.Forbidden;
        var address = await GetAddressAsync(repositoryId, cancellationToken);
        if (address is null) return IssueMutationResult.NotFound;
        _dbContext.IssueEdits.Add(new GitCandyIssueEdit
        {
            IssueId = issue.Id,
            CommentId = comment.Id,
            EditorUserId = actorUserId,
            PreviousMarkdown = comment.BodyMarkdown,
            PreviousHtml = comment.BodyHtml,
            EditedAtUtc = UtcNow
        });
        comment.BodyMarkdown = body.Trim();
        comment.BodyHtml = _markdownRenderer.Render(body, address.Value.NamespaceSlug, address.Value.RepositorySlug);
        comment.EditedAtUtc = UtcNow;
        comment.Version++;
        Touch(issue);
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<IssueMutationResult> HideCommentAsync(long repositoryId, long number, long commentId, string actorUserId, bool isOwner, CancellationToken cancellationToken = default)
    {
        if (!isOwner) return IssueMutationResult.Forbidden;
        var issue = await FindIssueAsync(repositoryId, number, cancellationToken);
        if (issue is null) return IssueMutationResult.NotFound;
        var comment = await _dbContext.IssueComments.SingleOrDefaultAsync(item => item.Id == commentId && item.IssueId == issue.Id, cancellationToken);
        if (comment is null) return IssueMutationResult.NotFound;
        comment.IsHidden = true;
        comment.HiddenByUserId = actorUserId;
        comment.HiddenAtUtc = UtcNow;
        comment.Version++;
        issue.Timeline.Add(NewEvent(IssueEventType.Hidden, actorUserId, UtcNow, "Comment hidden.", comment));
        Touch(issue);
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<IssueMutationResult> SetLockedAsync(long repositoryId, long number, string actorUserId, bool isOwner, bool locked, CancellationToken cancellationToken = default)
    {
        if (!isOwner) return IssueMutationResult.Forbidden;
        var issue = await FindIssueAsync(repositoryId, number, cancellationToken);
        if (issue is null) return IssueMutationResult.NotFound;
        if (issue.IsLocked == locked) return IssueMutationResult.Succeeded;
        issue.IsLocked = locked;
        issue.Timeline.Add(NewEvent(locked ? IssueEventType.Locked : IssueEventType.Unlocked, actorUserId, UtcNow));
        Touch(issue);
        return await SaveMutationAsync(cancellationToken);
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private Task<GitCandyIssue?> FindIssueAsync(long repositoryId, long number, CancellationToken cancellationToken) =>
        _dbContext.Issues.Include(item => item.Timeline).Include(item => item.Subscriptions)
            .SingleOrDefaultAsync(item => item.RepositoryId == repositoryId && item.Number == number, cancellationToken);

    private async Task<(string NamespaceSlug, string RepositorySlug)?> GetAddressAsync(long repositoryId, CancellationToken cancellationToken)
    {
        var value = await _dbContext.Repositories.AsNoTracking().Where(item => item.Id == repositoryId)
            .Select(item => new ValueTuple<string, string>(item.Namespace!.Slug, item.Name))
            .SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrEmpty(value.Item1) ? null : value;
    }

    private static void ValidateTitleAndBody(string title, string body)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > SchemaLimits.IssueTitle)
            throw new IssueValidationException($"Title must contain 1 to {SchemaLimits.IssueTitle} characters.");
        if (body.Length > SchemaLimits.IssueBody)
            throw new IssueValidationException($"Body cannot exceed {SchemaLimits.IssueBody} characters.");
    }

    private void Touch(GitCandyIssue issue)
    {
        issue.UpdatedAtUtc = UtcNow;
        issue.Version++;
    }

    private static GitCandyIssueTimelineEvent NewEvent(IssueEventType type, string? actor, DateTime now, string? detail = null, GitCandyIssueComment? comment = null) =>
        new() { Type = type, ActorUserId = actor, CreatedAtUtc = now, Detail = detail, Comment = comment };

    private async Task<IssueMutationResult> SaveMutationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return IssueMutationResult.Succeeded;
        }
        catch (DbUpdateConcurrencyException)
        {
            return IssueMutationResult.Conflict;
        }
    }

    private async Task EnforceRateLimitAsync(string actorUserId, CancellationToken cancellationToken)
    {
        var since = UtcNow.AddMinutes(-1);
        var count = await _dbContext.IssueTimelineEvents.AsNoTracking().CountAsync(
            item => item.ActorUserId == actorUserId && item.CreatedAtUtc >= since
                && (item.Type == IssueEventType.Created || item.Type == IssueEventType.Commented), cancellationToken);
        if (count >= MaxDiscussionActionsPerMinute)
            throw new IssueRateLimitException("Too many discussion actions. Try again shortly.");
    }

    private static void ValidateMentionFanOut(string body)
    {
        if (MentionRegex().Matches(body).Cast<Match>().Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxMentionFanOut + 1).Count() > MaxMentionFanOut)
            throw new IssueValidationException($"A post cannot mention more than {MaxMentionFanOut} users.");
    }

    private static IssueLabelSummary ToSummary(GitCandyIssueLabel label) =>
        new(label.Id, label.Name, label.Color, label.Description, label.IsArchived);

    private async Task<bool> CanReadAsUserAsync(long repositoryId, string userId, CancellationToken cancellationToken) =>
        await _permissionQuery.CanReadRepositoryAsync(
            repositoryId,
            userId,
            await IsAdministratorAsync(userId, cancellationToken),
            cancellationToken);

    private static bool IsTransientDiscussionConflict(Exception exception) =>
        exception is DbUpdateException
        || (string.Equals(exception.GetType().FullName, "Microsoft.Data.Sqlite.SqliteException", StringComparison.Ordinal)
            && (exception.Message.Contains("locked", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("busy", StringComparison.OrdinalIgnoreCase)));

    [GeneratedRegex(@"(?<![\w])@([A-Za-z0-9][A-Za-z0-9_.-]{0,63})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex MentionRegex();
}
