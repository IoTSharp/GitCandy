using System.Text.RegularExpressions;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Issues;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed partial class IssueService
{
    public async Task<IssueMutationResult> SetSubscriptionAsync(long repositoryId, long number, string userId, bool subscribed, CancellationToken cancellationToken = default)
    {
        var issue = await FindIssueAsync(repositoryId, number, cancellationToken);
        if (issue is null) return IssueMutationResult.NotFound;
        if (!await CanReadAsUserAsync(repositoryId, userId, cancellationToken)) return IssueMutationResult.Forbidden;
        var subscription = issue.Subscriptions.SingleOrDefault(item => item.UserId == userId);
        if (subscription is null)
        {
            issue.Subscriptions.Add(new GitCandyIssueSubscription { UserId = userId, IsSubscribed = subscribed, UpdatedAtUtc = UtcNow });
        }
        else
        {
            subscription.IsSubscribed = subscribed;
            subscription.UpdatedAtUtc = UtcNow;
        }
        issue.Timeline.Add(NewEvent(subscribed ? IssueEventType.Subscribed : IssueEventType.Unsubscribed, userId, UtcNow));
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<IssueMutationResult> SetAssigneeAsync(long repositoryId, long number, string actorUserId, bool isOwner, string? assigneeUserId, CancellationToken cancellationToken = default)
    {
        if (!isOwner) return IssueMutationResult.Forbidden;
        var issue = await FindIssueAsync(repositoryId, number, cancellationToken);
        if (issue is null) return IssueMutationResult.NotFound;
        if (assigneeUserId is not null && !await IsEligibleAssigneeAsync(repositoryId, assigneeUserId, cancellationToken)) return IssueMutationResult.Invalid;
        issue.AssigneeUserId = string.IsNullOrWhiteSpace(assigneeUserId) ? null : assigneeUserId;
        issue.Timeline.Add(NewEvent(issue.AssigneeUserId is null ? IssueEventType.Unassigned : IssueEventType.Assigned, actorUserId, UtcNow));
        Touch(issue);
        if (issue.AssigneeUserId is not null)
            await NotifyParticipantsAsync(issue, actorUserId, IssueNotificationType.Assignment, [issue.AssigneeUserId], cancellationToken);
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<IssueMutationResult> SetMilestoneAsync(long repositoryId, long number, string actorUserId, bool isOwner, long? milestoneId, CancellationToken cancellationToken = default)
    {
        if (!isOwner) return IssueMutationResult.Forbidden;
        var issue = await FindIssueAsync(repositoryId, number, cancellationToken);
        if (issue is null) return IssueMutationResult.NotFound;
        if (milestoneId is not null && !await _dbContext.IssueMilestones.AnyAsync(item => item.Id == milestoneId && item.RepositoryId == repositoryId && !item.IsArchived, cancellationToken))
            return IssueMutationResult.Invalid;
        issue.MilestoneId = milestoneId;
        issue.Timeline.Add(NewEvent(milestoneId is null ? IssueEventType.Demilestoned : IssueEventType.Milestoned, actorUserId, UtcNow));
        Touch(issue);
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<IssueMutationResult> SetLabelAsync(long repositoryId, long number, string actorUserId, bool isOwner, long labelId, bool selected, CancellationToken cancellationToken = default)
    {
        if (!isOwner) return IssueMutationResult.Forbidden;
        var issue = await _dbContext.Issues.Include(item => item.LabelLinks).Include(item => item.Timeline)
            .SingleOrDefaultAsync(item => item.RepositoryId == repositoryId && item.Number == number, cancellationToken);
        if (issue is null) return IssueMutationResult.NotFound;
        if (!await _dbContext.IssueLabels.AnyAsync(item => item.Id == labelId && item.RepositoryId == repositoryId && !item.IsArchived, cancellationToken))
            return IssueMutationResult.Invalid;
        var link = issue.LabelLinks.SingleOrDefault(item => item.LabelId == labelId);
        if (selected && link is null) issue.LabelLinks.Add(new GitCandyIssueLabelLink { LabelId = labelId });
        if (!selected && link is not null) _dbContext.IssueLabelLinks.Remove(link);
        issue.Timeline.Add(NewEvent(selected ? IssueEventType.Labeled : IssueEventType.Unlabeled, actorUserId, UtcNow, labelId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Touch(issue);
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<IssueMutationResult> AddRelationAsync(long repositoryId, long number, long targetNumber, string actorUserId, bool isOwner, IssueRelationType relationType, CancellationToken cancellationToken = default)
    {
        if (!isOwner || number == targetNumber) return IssueMutationResult.Forbidden;
        var issues = await _dbContext.Issues.Where(item => item.RepositoryId == repositoryId && (item.Number == number || item.Number == targetNumber)).ToArrayAsync(cancellationToken);
        if (issues.Length != 2) return IssueMutationResult.NotFound;
        var source = issues.Single(item => item.Number == number);
        var target = issues.Single(item => item.Number == targetNumber);
        if (await _dbContext.IssueRelations.AnyAsync(item => item.SourceIssueId == source.Id && item.TargetIssueId == target.Id && item.Type == relationType, cancellationToken))
            return IssueMutationResult.Succeeded;
        _dbContext.IssueRelations.Add(new GitCandyIssueRelation
        {
            RepositoryId = repositoryId,
            SourceIssueId = source.Id,
            TargetIssueId = target.Id,
            Type = relationType,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = UtcNow
        });
        var timelineEvent = NewEvent(IssueEventType.Related, actorUserId, UtcNow, $"{relationType} #{targetNumber}");
        timelineEvent.IssueId = source.Id;
        _dbContext.IssueTimelineEvents.Add(timelineEvent);
        await NotifyParticipantsAsync(target, actorUserId, IssueNotificationType.Relation, [], cancellationToken);
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<IssueRepositoryMetadata> GetMetadataAsync(long repositoryId, CancellationToken cancellationToken = default)
    {
        var labels = await _dbContext.IssueLabels.AsNoTracking().Where(item => item.RepositoryId == repositoryId)
            .OrderBy(item => item.NormalizedName).Select(item => new IssueLabelSummary(item.Id, item.Name, item.Color, item.Description, item.IsArchived)).ToArrayAsync(cancellationToken);
        var milestones = await _dbContext.IssueMilestones.AsNoTracking().Where(item => item.RepositoryId == repositoryId)
            .OrderBy(item => item.IsClosed).ThenBy(item => item.DueAtUtc).ThenBy(item => item.Title)
            .Select(item => new IssueMilestoneSummary(item.Id, item.Title, item.Description, item.DueAtUtc, item.IsClosed, item.IsArchived,
                item.Issues.Count(issue => issue.State == IssueState.Open), item.Issues.Count(issue => issue.State == IssueState.Closed)))
            .ToArrayAsync(cancellationToken);
        var assigneeIds = _dbContext.UserRepositoryRoles.AsNoTracking().Where(item => item.RepositoryId == repositoryId && item.AllowRead).Select(item => item.UserId)
            .Union(from repositoryRole in _dbContext.TeamRepositoryRoles.AsNoTracking()
                   join teamRole in _dbContext.UserTeamRoles.AsNoTracking() on repositoryRole.TeamId equals teamRole.TeamId
                   where repositoryRole.RepositoryId == repositoryId && repositoryRole.AllowRead
                   select teamRole.UserId);
        var assignees = await _dbContext.Users.AsNoTracking().Where(item => assigneeIds.Contains(item.Id)).OrderBy(item => item.NormalizedUserName)
            .Select(item => new IssueAssigneeSummary(item.Id, item.UserName ?? string.Empty)).ToArrayAsync(cancellationToken);
        return new IssueRepositoryMetadata(labels, milestones, assignees);
    }

    public async Task<IssueLabelSummary?> SaveLabelAsync(long repositoryId, long? labelId, string name, string color, string description, bool isOwner, CancellationToken cancellationToken = default)
    {
        if (!isOwner || string.IsNullOrWhiteSpace(name) || name.Length > SchemaLimits.IssueLabelName || !LabelColorRegex().IsMatch(color) || description.Length > SchemaLimits.IssueLabelDescription)
            return null;
        var normalizedName = name.Trim().ToUpperInvariant();
        var label = labelId is null ? null : await _dbContext.IssueLabels.SingleOrDefaultAsync(item => item.Id == labelId && item.RepositoryId == repositoryId, cancellationToken);
        if (labelId is not null && label is null) return null;
        if (label is null)
        {
            label = new GitCandyIssueLabel { RepositoryId = repositoryId };
            _dbContext.IssueLabels.Add(label);
        }
        label.Name = name.Trim(); label.NormalizedName = normalizedName; label.Color = color.ToLowerInvariant(); label.Description = description.Trim(); label.IsArchived = false;
        try { await _dbContext.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { return null; }
        return ToSummary(label);
    }

    public async Task<IssueMilestoneSummary?> SaveMilestoneAsync(long repositoryId, long? milestoneId, string title, string description, DateTime? dueAtUtc, bool isOwner, CancellationToken cancellationToken = default)
    {
        if (!isOwner || string.IsNullOrWhiteSpace(title) || title.Length > SchemaLimits.IssueMilestoneTitle || description.Length > SchemaLimits.IssueMilestoneDescription) return null;
        var milestone = milestoneId is null ? null : await _dbContext.IssueMilestones.SingleOrDefaultAsync(item => item.Id == milestoneId && item.RepositoryId == repositoryId, cancellationToken);
        if (milestoneId is not null && milestone is null) return null;
        if (milestone is null) { milestone = new GitCandyIssueMilestone { RepositoryId = repositoryId }; _dbContext.IssueMilestones.Add(milestone); }
        milestone.Title = title.Trim(); milestone.Description = description.Trim(); milestone.DueAtUtc = dueAtUtc; milestone.IsArchived = false;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new IssueMilestoneSummary(milestone.Id, milestone.Title, milestone.Description, milestone.DueAtUtc, milestone.IsClosed, milestone.IsArchived, 0, 0);
    }

    public async Task<IssueMutationResult> ArchiveLabelAsync(long repositoryId, long labelId, bool isOwner, CancellationToken cancellationToken = default)
    {
        if (!isOwner) return IssueMutationResult.Forbidden;
        var label = await _dbContext.IssueLabels.SingleOrDefaultAsync(item => item.Id == labelId && item.RepositoryId == repositoryId, cancellationToken);
        if (label is null) return IssueMutationResult.NotFound;
        label.IsArchived = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return IssueMutationResult.Succeeded;
    }

    public async Task<IssueMutationResult> SetMilestoneStatusAsync(long repositoryId, long milestoneId, bool closed, bool archived, bool isOwner, CancellationToken cancellationToken = default)
    {
        if (!isOwner) return IssueMutationResult.Forbidden;
        var milestone = await _dbContext.IssueMilestones.SingleOrDefaultAsync(item => item.Id == milestoneId && item.RepositoryId == repositoryId, cancellationToken);
        if (milestone is null) return IssueMutationResult.NotFound;
        milestone.IsClosed = closed;
        milestone.IsArchived = archived;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return IssueMutationResult.Succeeded;
    }

    private async Task ApplyInitialMetadataAsync(GitCandyIssue issue, CreateIssueCommand command, CancellationToken cancellationToken)
    {
        if (command.AssigneeUserId is not null && await IsEligibleAssigneeAsync(issue.RepositoryId, command.AssigneeUserId, cancellationToken))
            issue.AssigneeUserId = command.AssigneeUserId;
        if (command.MilestoneId is not null && await _dbContext.IssueMilestones.AnyAsync(item => item.Id == command.MilestoneId && item.RepositoryId == issue.RepositoryId && !item.IsArchived, cancellationToken))
            issue.MilestoneId = command.MilestoneId;
        if (command.LabelIds is { Count: > 0 })
        {
            var ids = await _dbContext.IssueLabels.Where(item => item.RepositoryId == issue.RepositoryId && !item.IsArchived && command.LabelIds.Contains(item.Id)).Select(item => item.Id).ToArrayAsync(cancellationToken);
            foreach (var id in ids) issue.LabelLinks.Add(new GitCandyIssueLabelLink { LabelId = id });
        }
    }

    private async Task<bool> IsEligibleAssigneeAsync(long repositoryId, string userId, CancellationToken cancellationToken)
    {
        if (await _dbContext.UserRepositoryRoles.AnyAsync(
            item => item.RepositoryId == repositoryId && item.UserId == userId && item.AllowRead,
            cancellationToken))
        {
            return true;
        }

        return await (from repositoryRole in _dbContext.TeamRepositoryRoles
            join teamRole in _dbContext.UserTeamRoles on repositoryRole.TeamId equals teamRole.TeamId
            where repositoryRole.RepositoryId == repositoryId
                && repositoryRole.AllowRead
                && teamRole.UserId == userId
            select teamRole).AnyAsync(cancellationToken);
    }

    [GeneratedRegex("^[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LabelColorRegex();
}
