using System.Data;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Governance;
using GitCandy.PullRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GitCandy.Application;

internal sealed partial class PullRequestService
{
    public async Task<PullRequestMergeability?> GetMergeabilityAsync(
        long repositoryId,
        long number,
        CancellationToken cancellationToken = default)
    {
        await RefreshPullRequestAsync(repositoryId, number, cancellationToken);
        var pullRequest = await LoadMergePullRequestAsync(repositoryId, number, cancellationToken);
        return pullRequest is null
            ? null
            : await BuildMergeabilityAsync(pullRequest, cancellationToken);
    }

    public async Task<PullRequestMergeResult> MergePullRequestAsync(
        long repositoryId,
        long number,
        MergePullRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.ActorUserId)
            || string.IsNullOrWhiteSpace(command.ActorName)
            || string.IsNullOrWhiteSpace(command.ActorEmail)
            || string.IsNullOrWhiteSpace(command.Message)
            || command.Message.Length > SchemaLimits.IssueBody)
        {
            return new PullRequestMergeResult(PullRequestMutationResult.Invalid);
        }

        if (!await CanWriteAsUserAsync(repositoryId, command.ActorUserId, cancellationToken))
        {
            return new PullRequestMergeResult(PullRequestMutationResult.Forbidden);
        }

        var refresh = await RefreshPullRequestAsync(repositoryId, number, cancellationToken);
        if (refresh is not (PullRequestMutationResult.Succeeded or PullRequestMutationResult.BranchNotFound))
        {
            return new PullRequestMergeResult(refresh);
        }

        var pullRequest = await LoadMergePullRequestAsync(repositoryId, number, cancellationToken);
        if (pullRequest is null)
        {
            return new PullRequestMergeResult(PullRequestMutationResult.NotFound);
        }

        if (pullRequest.Version != command.Version)
        {
            return new PullRequestMergeResult(PullRequestMutationResult.Conflict);
        }

        var mergeability = await BuildMergeabilityAsync(pullRequest, cancellationToken);
        if (!mergeability.IsMergeable
            || pullRequest.Repository is null
            || pullRequest.SourceRepository is null)
        {
            return new PullRequestMergeResult(PullRequestMutationResult.Conflict);
        }

        var mergeContext = new PullRequestMergeContext(
            repositoryId,
            pullRequest.Number,
            pullRequest.SourceBranch,
            pullRequest.TargetBranch,
            pullRequest.CurrentBaseSha,
            pullRequest.CurrentHeadSha,
            command.Method,
            command.ActorUserId,
            pullRequest.Repository.StorageName);
        foreach (var hook in _mergeHooks)
        {
            try
            {
                var validation = await hook.ValidateAsync(mergeContext, cancellationToken);
                if (validation != PullRequestMutationResult.Succeeded)
                {
                    return new PullRequestMergeResult(PullRequestMutationResult.HookRejected);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Pull Request merge hook {HookType} rejected repository {RepositoryId} PR {PullRequestNumber}.",
                    hook.GetType().Name,
                    repositoryId,
                    number);
                return new PullRequestMergeResult(PullRequestMutationResult.HookRejected);
            }
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        pullRequest.Timeline.Add(NewEvent(
            PullRequestEventType.MergeStarted,
            command.ActorUserId,
            UtcNow,
            command.Method.ToString()));
        var gitResult = _gitRepository.Merge(
            pullRequest.SourceRepository.StorageName,
            pullRequest.SourceBranch,
            pullRequest.Repository.StorageName,
            pullRequest.TargetBranch,
            pullRequest.Number,
            pullRequest.CurrentBaseSha,
            pullRequest.CurrentHeadSha,
            command.Method,
            command.Message.Trim(),
            command.ActorName.Trim(),
            command.ActorEmail.Trim(),
            _timeProvider.GetUtcNow(),
            cancellationToken);
        if (gitResult.Result != PullRequestMutationResult.Succeeded || gitResult.CommitSha is null)
        {
            return new PullRequestMergeResult(gitResult.Result);
        }

        try
        {
            var mergedAt = UtcNow;
            pullRequest.State = PullRequestState.Merged;
            pullRequest.ActivePairKey = $"merged:{pullRequest.Number}";
            pullRequest.IsDraft = false;
            pullRequest.MergeCommitSha = gitResult.CommitSha;
            pullRequest.MergedByUserId = command.ActorUserId;
            pullRequest.MergedAtUtc = mergedAt;
            pullRequest.ClosedAtUtc = mergedAt;
            pullRequest.CurrentBaseSha = gitResult.CommitSha;
            Touch(pullRequest);
            pullRequest.Timeline.Add(NewEvent(
                PullRequestEventType.Merged,
                command.ActorUserId,
                mergedAt,
                $"{command.Method}: {gitResult.CommitSha}"));
            await _dbContext.SaveChangesAsync(cancellationToken);

            var closedIssues = await _issueService.ApplyClosingReferencesAsync(
                repositoryId,
                command.ActorUserId,
                $"{pullRequest.Title}\n{pullRequest.BodyMarkdown}\n{command.Message}",
                $"Pull Request #{pullRequest.Number}",
                cancellationToken);
            if (closedIssues > 0)
            {
                pullRequest.Timeline.Add(NewEvent(
                    PullRequestEventType.IssuesClosed,
                    command.ActorUserId,
                    UtcNow,
                    $"{closedIssues} issue(s)"));
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            var mergedEvent = new PullRequestMergedEvent(
                mergeContext,
                gitResult.CommitSha,
                new DateTimeOffset(mergedAt, TimeSpan.Zero));
            foreach (var hook in _mergeHooks)
            {
                try
                {
                    await hook.OnMergedAsync(mergedEvent, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(
                        exception,
                        "Post-merge hook {HookType} failed for repository {RepositoryId} PR {PullRequestNumber}; the merge remains committed.",
                        hook.GetType().Name,
                        repositoryId,
                        number);
                }
            }

            return new PullRequestMergeResult(
                PullRequestMutationResult.Succeeded,
                gitResult.CommitSha,
                closedIssues);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _gitRepository.RollbackMerge(
                pullRequest.Repository.StorageName,
                pullRequest.TargetBranch,
                gitResult.CommitSha,
                mergeability.TargetSha,
                CancellationToken.None);
            throw;
        }
    }

    private Task<GitCandyPullRequest?> LoadMergePullRequestAsync(
        long repositoryId,
        long number,
        CancellationToken cancellationToken) =>
        _dbContext.PullRequests
            .AsSplitQuery()
            .Include(item => item.Repository)
            .Include(item => item.SourceRepository)
            .Include(item => item.Timeline)
            .Include(item => item.Reviews)
            .Include(item => item.ReviewThreads)
            .SingleOrDefaultAsync(
                item => item.RepositoryId == repositoryId && item.Number == number,
                cancellationToken);

    private async Task<PullRequestMergeability> BuildMergeabilityAsync(
        GitCandyPullRequest pullRequest,
        CancellationToken cancellationToken)
    {
        if (pullRequest.Repository is null || pullRequest.SourceRepository is null)
        {
            return Blocked(PullRequestMergeabilityState.MissingSource, pullRequest, "The source repository is unavailable.");
        }

        var git = _gitRepository.EvaluateMergeability(
            pullRequest.SourceRepository.StorageName,
            pullRequest.SourceBranch,
            pullRequest.Repository.StorageName,
            pullRequest.TargetBranch,
            pullRequest.Number,
            pullRequest.CurrentBaseSha,
            pullRequest.CurrentHeadSha,
            cancellationToken);
        var latestDecisions = pullRequest.Reviews
            .Where(item => item.DismissedAtUtc is null && item.State != PullRequestReviewState.Commented)
            .GroupBy(item => item.ReviewerUserId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.SubmittedAtUtc).ThenByDescending(item => item.Id).First())
            .ToArray();
        var effectiveApprovals = latestDecisions.Count(item =>
            item.State == PullRequestReviewState.Approved
            && (!_applicationOptions.DismissStalePullRequestApprovals
                || string.Equals(item.HeadSha, pullRequest.CurrentHeadSha, StringComparison.Ordinal)));
        var hasChangesRequested = latestDecisions.Any(item =>
            item.State == PullRequestReviewState.ChangesRequested
            && (!_applicationOptions.DismissStalePullRequestApprovals
                || string.Equals(item.HeadSha, pullRequest.CurrentHeadSha, StringComparison.Ordinal)));
        var unresolvedThreads = pullRequest.ReviewThreads.Count(item => !item.IsResolved && !item.IsOutdated);
        var codeOwners = _gitRepository.ReadCodeOwnersSnapshot(
            pullRequest.Repository.StorageName,
            pullRequest.CurrentBaseSha,
            pullRequest.CurrentHeadSha,
            cancellationToken);
        var gate = await _pushGate.EvaluateAsync(
            new GitPushGateRequest(
                pullRequest.RepositoryId,
                new GitRefActor("mergeability"),
                GitRefOperation.Merge,
                [new GitRefUpdate(
                    pullRequest.CurrentBaseSha,
                    pullRequest.CurrentHeadSha,
                    $"refs/heads/{pullRequest.TargetBranch}")],
                new GitPushReviewContext(pullRequest.Number, codeOwners),
                RecordAudit: false,
                EvaluateAccess: false),
            cancellationToken);
        var requiredApprovals = gate.Review?.RequiredApprovals
            ?? Math.Max(0, _applicationOptions.RequiredPullRequestApprovals);
        effectiveApprovals = gate.Review?.EffectiveApprovals ?? effectiveApprovals;
        var blockers = new List<(PullRequestMergeabilityState State, string Reason)>();

        if (pullRequest.State != PullRequestState.Open) blockers.Add((PullRequestMergeabilityState.Closed, "The Pull Request is not open."));
        if (pullRequest.IsDraft) blockers.Add((PullRequestMergeabilityState.Draft, "Mark the Pull Request ready for review."));
        if (!git.SourceExists) blockers.Add((PullRequestMergeabilityState.MissingSource, "The source branch no longer exists."));
        if (!git.TargetExists) blockers.Add((PullRequestMergeabilityState.MissingTarget, "The target branch no longer exists."));
        if (git.SourceExists && !git.SourceMatches) blockers.Add((PullRequestMergeabilityState.SourceChanged, "The source branch changed; refresh and review the new head."));
        if (git.TargetExists && !git.TargetMatches) blockers.Add((PullRequestMergeabilityState.TargetChanged, "The target branch changed; refresh mergeability."));
        if (git.HasConflicts) blockers.Add((PullRequestMergeabilityState.Conflicting, "The branches have merge conflicts."));
        if (hasChangesRequested) blockers.Add((PullRequestMergeabilityState.ChangesRequested, "A reviewer requested changes."));
        if (unresolvedThreads > 0) blockers.Add((PullRequestMergeabilityState.UnresolvedThreads, $"Resolve {unresolvedThreads} active review thread(s)."));
        foreach (var reason in gate.Reasons)
        {
            var state = reason.Contains("required check", StringComparison.Ordinal)
                ? PullRequestMergeabilityState.ChecksBlocked
                : reason.Contains("approval", StringComparison.OrdinalIgnoreCase)
                    || reason.Contains("CODEOWNERS", StringComparison.Ordinal)
                    || reason.Contains("review", StringComparison.OrdinalIgnoreCase)
                    ? PullRequestMergeabilityState.ApprovalRequired
                    : PullRequestMergeabilityState.GovernanceBlocked;
            blockers.Add((state, reason));
        }

        return new PullRequestMergeability(
            blockers.Count == 0 ? PullRequestMergeabilityState.Mergeable : blockers[0].State,
            blockers.Count == 0,
            git.CurrentSourceSha ?? pullRequest.CurrentHeadSha,
            git.CurrentTargetSha ?? pullRequest.CurrentBaseSha,
            git.HasConflicts,
            effectiveApprovals,
            requiredApprovals,
            unresolvedThreads,
            hasChangesRequested,
            gate.RequiredChecksSatisfied,
            blockers.Select(item => item.Reason).ToArray());
    }

    private static PullRequestMergeability Blocked(
        PullRequestMergeabilityState state,
        GitCandyPullRequest pullRequest,
        string reason) =>
        new(
            state,
            IsMergeable: false,
            pullRequest.CurrentHeadSha,
            pullRequest.CurrentBaseSha,
            HasConflicts: false,
            EffectiveApprovals: 0,
            RequiredApprovals: 0,
            UnresolvedThreads: 0,
            HasChangesRequested: false,
            RequiredChecksSatisfied: true,
            [reason]);
}
