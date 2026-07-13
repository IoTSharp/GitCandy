using GitCandy.Integrations;
using GitCandy.PullRequests;

namespace GitCandy.Application;

internal sealed class WebhookPullRequestMergeHook(IIntegrationEventPublisher eventPublisher) : IPullRequestMergeHook
{
    private readonly IIntegrationEventPublisher _eventPublisher = eventPublisher;

    public Task<PullRequestMutationResult> ValidateAsync(
        PullRequestMergeContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PullRequestMutationResult.Succeeded);

    public Task OnMergedAsync(
        PullRequestMergedEvent mergedEvent,
        CancellationToken cancellationToken = default) =>
        _eventPublisher.PublishPullRequestMergedAsync(mergedEvent, cancellationToken);
}
