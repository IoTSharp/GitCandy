using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.PullRequests;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed partial class PullRequestService
{
    public async Task<PullRequestReviewOverview?> GetReviewOverviewAsync(
        long repositoryId,
        long number,
        CancellationToken cancellationToken = default)
    {
        var pullRequest = await _dbContext.PullRequests.AsNoTracking().AsSplitQuery()
            .Include(item => item.Assignee)
            .Include(item => item.Reviewers).ThenInclude(item => item.Reviewer)
            .Include(item => item.Reviewers).ThenInclude(item => item.RequestedBy)
            .Include(item => item.Reviews).ThenInclude(item => item.Reviewer)
            .Include(item => item.Reviews).ThenInclude(item => item.DismissedBy)
            .SingleOrDefaultAsync(
                item => item.RepositoryId == repositoryId && item.Number == number,
                cancellationToken);
        if (pullRequest is null)
        {
            return null;
        }

        var candidates = await GetEligibleParticipantsAsync(repositoryId, cancellationToken);
        var reviews = pullRequest.Reviews
            .OrderBy(item => item.SubmittedAtUtc)
            .ThenBy(item => item.Id)
            .Select(item => ToReview(item, pullRequest.CurrentHeadSha))
            .ToArray();
        var reviewers = pullRequest.Reviewers
            .OrderBy(item => item.Reviewer!.NormalizedUserName)
            .Select(item =>
            {
                var latestEntity = pullRequest.Reviews
                    .Where(review => review.ReviewerUserId == item.ReviewerUserId)
                    .OrderByDescending(review => review.SubmittedAtUtc)
                    .ThenByDescending(review => review.Id)
                    .FirstOrDefault();
                return new PullRequestReviewerStatus(
                    item.ReviewerUserId,
                    item.Reviewer?.UserName ?? string.Empty,
                    item.RequestedBy?.UserName ?? string.Empty,
                    item.RequestedAtUtc,
                    latestEntity is null || latestEntity.ReviewerRequestVersion < item.Version,
                    latestEntity is null ? null : ToReview(latestEntity, pullRequest.CurrentHeadSha));
            })
            .ToArray();

        return new PullRequestReviewOverview(
            pullRequest.AssigneeUserId,
            pullRequest.Assignee?.UserName,
            candidates,
            reviewers,
            reviews,
            ReviewPolicy);
    }

    public async Task<PullRequestMutationResult> SetAssigneeAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        string? assigneeUserId,
        CancellationToken cancellationToken = default)
    {
        var pullRequest = await FindPullRequestAsync(repositoryId, number, cancellationToken);
        if (pullRequest is null)
        {
            return PullRequestMutationResult.NotFound;
        }

        if (!isOwner && !string.Equals(pullRequest.AuthorUserId, actorUserId, StringComparison.Ordinal))
        {
            return PullRequestMutationResult.Forbidden;
        }

        var normalizedAssigneeId = string.IsNullOrWhiteSpace(assigneeUserId) ? null : assigneeUserId;
        if (normalizedAssigneeId is not null
            && !await IsEligibleParticipantAsync(repositoryId, normalizedAssigneeId, cancellationToken))
        {
            return PullRequestMutationResult.Invalid;
        }

        if (string.Equals(pullRequest.AssigneeUserId, normalizedAssigneeId, StringComparison.Ordinal))
        {
            return PullRequestMutationResult.Succeeded;
        }

        pullRequest.AssigneeUserId = normalizedAssigneeId;
        Touch(pullRequest);
        pullRequest.Timeline.Add(NewEvent(
            PullRequestEventType.AssigneeChanged,
            actorUserId,
            UtcNow,
            normalizedAssigneeId is null ? "unassigned" : await GetUserNameAsync(normalizedAssigneeId, cancellationToken)));
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<PullRequestMutationResult> RequestReviewAsync(
        long repositoryId,
        long number,
        string actorUserId,
        bool isOwner,
        string reviewerUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewerUserId))
        {
            return PullRequestMutationResult.Invalid;
        }

        var pullRequest = await _dbContext.PullRequests
            .Include(item => item.Timeline)
            .Include(item => item.Reviewers)
            .SingleOrDefaultAsync(
                item => item.RepositoryId == repositoryId && item.Number == number,
                cancellationToken);
        if (pullRequest is null)
        {
            return PullRequestMutationResult.NotFound;
        }

        if ((!isOwner && !string.Equals(pullRequest.AuthorUserId, actorUserId, StringComparison.Ordinal))
            || pullRequest.State != PullRequestState.Open)
        {
            return PullRequestMutationResult.Forbidden;
        }

        if (!await IsEligibleParticipantAsync(repositoryId, reviewerUserId, cancellationToken))
        {
            return PullRequestMutationResult.Invalid;
        }

        var reviewer = pullRequest.Reviewers.SingleOrDefault(item => item.ReviewerUserId == reviewerUserId);
        var isRerequest = reviewer is not null;
        if (reviewer is null)
        {
            reviewer = new GitCandyPullRequestReviewer
            {
                ReviewerUserId = reviewerUserId,
                RequestedByUserId = actorUserId,
                RequestedAtUtc = UtcNow,
                Version = 1
            };
            pullRequest.Reviewers.Add(reviewer);
        }
        else
        {
            reviewer.RequestedByUserId = actorUserId;
            reviewer.RequestedAtUtc = UtcNow;
            reviewer.Version++;
        }

        Touch(pullRequest);
        pullRequest.Timeline.Add(NewEvent(
            isRerequest ? PullRequestEventType.ReviewRerequested : PullRequestEventType.ReviewRequested,
            actorUserId,
            UtcNow,
            await GetUserNameAsync(reviewerUserId, cancellationToken)));
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<PullRequestMutationResult> SubmitReviewAsync(
        long repositoryId,
        long number,
        string reviewerUserId,
        SubmitPullRequestReviewCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidReview(command))
        {
            return PullRequestMutationResult.Invalid;
        }

        if (!await CanReadAsUserAsync(repositoryId, reviewerUserId, cancellationToken))
        {
            return PullRequestMutationResult.Forbidden;
        }

        var pullRequest = await _dbContext.PullRequests
            .Include(item => item.Repository)
            .Include(item => item.Timeline)
            .Include(item => item.Reviewers)
            .SingleOrDefaultAsync(
                item => item.RepositoryId == repositoryId && item.Number == number,
                cancellationToken);
        if (pullRequest is null)
        {
            return PullRequestMutationResult.NotFound;
        }

        if (pullRequest.State != PullRequestState.Open)
        {
            return PullRequestMutationResult.Invalid;
        }

        var reviewer = pullRequest.Reviewers.SingleOrDefault(item => item.ReviewerUserId == reviewerUserId);
        if (reviewer is null)
        {
            return PullRequestMutationResult.Forbidden;
        }

        if (command.State == PullRequestReviewState.Approved
            && !_applicationOptions.AllowAuthorApproval
            && string.Equals(pullRequest.AuthorUserId, reviewerUserId, StringComparison.Ordinal))
        {
            return PullRequestMutationResult.Forbidden;
        }

        var address = await GetAddressAsync(repositoryId, cancellationToken);
        if (address is null)
        {
            return PullRequestMutationResult.NotFound;
        }

        var now = UtcNow;
        pullRequest.Reviews.Add(new GitCandyPullRequestReview
        {
            ReviewerUserId = reviewerUserId,
            State = command.State,
            BodyMarkdown = command.Body.Trim(),
            BodyHtml = _markdownRenderer.Render(command.Body, address.Value.NamespaceSlug, address.Value.RepositorySlug),
            HeadSha = pullRequest.CurrentHeadSha,
            ReviewerRequestVersion = reviewer.Version,
            SubmittedAtUtc = now,
            Version = 1
        });
        Touch(pullRequest);
        pullRequest.Timeline.Add(NewEvent(
            PullRequestEventType.ReviewSubmitted,
            reviewerUserId,
            now,
            command.State.ToString()));
        return await SaveMutationAsync(cancellationToken);
    }

    public async Task<PullRequestMutationResult> DismissReviewAsync(
        long repositoryId,
        long number,
        long reviewId,
        string actorUserId,
        bool isOwner,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!isOwner || string.IsNullOrWhiteSpace(reason) || reason.Length > SchemaLimits.IssueDetail)
        {
            return isOwner ? PullRequestMutationResult.Invalid : PullRequestMutationResult.Forbidden;
        }

        var pullRequest = await _dbContext.PullRequests
            .Include(item => item.Timeline)
            .Include(item => item.Reviews)
            .SingleOrDefaultAsync(
                item => item.RepositoryId == repositoryId && item.Number == number,
                cancellationToken);
        if (pullRequest is null)
        {
            return PullRequestMutationResult.NotFound;
        }

        var review = pullRequest.Reviews.SingleOrDefault(item => item.Id == reviewId);
        if (review is null)
        {
            return PullRequestMutationResult.NotFound;
        }

        if (review.DismissedAtUtc is not null)
        {
            return PullRequestMutationResult.Succeeded;
        }

        review.DismissedByUserId = actorUserId;
        review.DismissedAtUtc = UtcNow;
        review.DismissalReason = reason.Trim();
        review.Version++;
        Touch(pullRequest);
        pullRequest.Timeline.Add(NewEvent(
            PullRequestEventType.ReviewDismissed,
            actorUserId,
            UtcNow,
            await GetUserNameAsync(review.ReviewerUserId, cancellationToken)));
        return await SaveMutationAsync(cancellationToken);
    }

    private PullRequestReviewPolicy ReviewPolicy => new(
        _applicationOptions.AllowAuthorApproval,
        _applicationOptions.DismissStalePullRequestApprovals);

    private PullRequestReview ToReview(GitCandyPullRequestReview item, string currentHeadSha)
    {
        var isStale = !string.Equals(item.HeadSha, currentHeadSha, StringComparison.Ordinal);
        var isDismissed = item.DismissedAtUtc is not null;
        return new PullRequestReview(
            item.Id,
            item.ReviewerUserId,
            item.Reviewer?.UserName ?? string.Empty,
            isDismissed ? PullRequestReviewState.Dismissed : item.State,
            item.BodyMarkdown,
            item.BodyHtml,
            item.HeadSha,
            isStale,
            !isDismissed && item.State == PullRequestReviewState.Approved
                && (!isStale || !_applicationOptions.DismissStalePullRequestApprovals),
            item.DismissedBy?.UserName,
            item.DismissedAtUtc,
            item.DismissalReason,
            item.SubmittedAtUtc);
    }

    private async Task<IReadOnlyList<PullRequestParticipant>> GetEligibleParticipantsAsync(
        long repositoryId,
        CancellationToken cancellationToken)
    {
        var userIds = EligibleParticipantIds(repositoryId);
        return await _dbContext.Users.AsNoTracking()
            .Where(item => userIds.Contains(item.Id))
            .OrderBy(item => item.NormalizedUserName)
            .Select(item => new PullRequestParticipant(item.Id, item.UserName ?? string.Empty))
            .ToArrayAsync(cancellationToken);
    }

    private Task<bool> IsEligibleParticipantAsync(
        long repositoryId,
        string userId,
        CancellationToken cancellationToken) =>
        EligibleParticipantIds(repositoryId).AnyAsync(item => item == userId, cancellationToken);

    private IQueryable<string> EligibleParticipantIds(long repositoryId) =>
        _dbContext.UserRepositoryRoles.AsNoTracking()
            .Where(item => item.RepositoryId == repositoryId && item.AllowRead)
            .Select(item => item.UserId)
            .Union(
                from repositoryRole in _dbContext.TeamRepositoryRoles.AsNoTracking()
                join teamRole in _dbContext.UserTeamRoles.AsNoTracking()
                    on repositoryRole.TeamId equals teamRole.TeamId
                where repositoryRole.RepositoryId == repositoryId && repositoryRole.AllowRead
                select teamRole.UserId);

    private Task<string> GetUserNameAsync(string userId, CancellationToken cancellationToken) =>
        _dbContext.Users.AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => item.UserName ?? string.Empty)
            .SingleAsync(cancellationToken);

    private static bool IsValidReview(SubmitPullRequestReviewCommand command) =>
        command.State is PullRequestReviewState.Commented
            or PullRequestReviewState.Approved
            or PullRequestReviewState.ChangesRequested
        && command.Body.Length <= SchemaLimits.IssueBody
        && (command.State == PullRequestReviewState.Approved || !string.IsNullOrWhiteSpace(command.Body));
}
