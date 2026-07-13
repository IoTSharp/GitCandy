using System.ComponentModel.DataAnnotations;
using GitCandy.Integrations;

namespace GitCandy.Models;

public sealed class RepositoryWebhooksViewModel
{
    public required string NamespaceSlug { get; init; }
    public required string RepositoryName { get; init; }
    public IReadOnlyList<WebhookSubscriptionSummary> Subscriptions { get; init; } = [];
    public IReadOnlyList<WebhookDeliverySummary> Deliveries { get; init; } = [];
    public CreateWebhookViewModel Create { get; init; } = new();

    public string CanonicalRepositoryPath =>
        $"/{Uri.EscapeDataString(NamespaceSlug)}/{Uri.EscapeDataString(RepositoryName)}";
}

public sealed class CreateWebhookViewModel
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(2048)]
    [Display(Name = "Target URL")]
    public string TargetUrl { get; set; } = string.Empty;

    [Display(Name = "Push")]
    public bool Push { get; set; } = true;

    [Display(Name = "Pull request merged")]
    public bool PullRequestMerged { get; set; } = true;

    [Display(Name = "Check updated")]
    public bool CheckUpdated { get; set; }

    [Display(Name = "Release published")]
    public bool ReleasePublished { get; set; }

    public WebhookEventTypes ToEvents()
    {
        var events = WebhookEventTypes.None;
        if (Push) events |= WebhookEventTypes.Push;
        if (PullRequestMerged) events |= WebhookEventTypes.PullRequestMerged;
        if (CheckUpdated) events |= WebhookEventTypes.CheckUpdated;
        if (ReleasePublished) events |= WebhookEventTypes.ReleasePublished;
        return events;
    }
}

public sealed record CreatedWebhookViewModel(
    string NamespaceSlug,
    string RepositoryName,
    CreatedWebhookSubscription Created)
{
    public string WebhooksPath =>
        $"/{Uri.EscapeDataString(NamespaceSlug)}/{Uri.EscapeDataString(RepositoryName)}/settings/webhooks";
}
