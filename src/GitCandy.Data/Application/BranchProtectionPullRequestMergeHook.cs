using GitCandy.Governance;
using GitCandy.PullRequests;

namespace GitCandy.Application;

internal sealed class BranchProtectionPullRequestMergeHook(IGitPushGate pushGate) : IPullRequestMergeHook
{
    private readonly IGitPushGate _pushGate = pushGate;

    public async Task<PullRequestMutationResult> ValidateAsync(
        PullRequestMergeContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await _pushGate.EvaluateAsync(
            new GitPushGateRequest(
                context.RepositoryId,
                new GitRefActor(context.ActorUserId, UserId: context.ActorUserId),
                GitRefOperation.Merge,
                [new GitRefUpdate(context.BaseSha, context.HeadSha, $"refs/heads/{context.TargetBranch}")]),
            cancellationToken);
        return result.Allowed
            ? PullRequestMutationResult.Succeeded
            : PullRequestMutationResult.HookRejected;
    }

    public Task OnMergedAsync(
        PullRequestMergedEvent mergedEvent,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
