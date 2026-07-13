using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Integrations;
using GitCandy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public sealed class RepositoryIntegrationsController(
    IRepositoryAddressResolver addressResolver,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser,
    IWebhookService webhookService) : Controller
{
    private readonly IRepositoryAddressResolver _addressResolver = addressResolver;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IWebhookService _webhookService = webhookService;

    [HttpGet("/{namespaceSlug}/{project}/settings/webhooks", Name = "canonical-repository-webhooks")]
    public async Task<IActionResult> Index(
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var access = await ResolveOwnerAsync(namespaceSlug, project, cancellationToken);
        return access.Result ?? await RenderAsync(access.Address!, new CreateWebhookViewModel(), cancellationToken);
    }

    [HttpPost("/{namespaceSlug}/{project}/settings/webhooks")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Create(
        string namespaceSlug,
        string project,
        [Bind(Prefix = "Create")] CreateWebhookViewModel model,
        CancellationToken cancellationToken)
    {
        var access = await ResolveOwnerAsync(namespaceSlug, project, cancellationToken);
        if (access.Result is not null) return access.Result;
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return await RenderAsync(access.Address!, model, cancellationToken);
        }
        var created = await _webhookService.CreateSubscriptionAsync(
            access.Address!.RepositoryId,
            _currentUser.UserId,
            new CreateWebhookSubscription(model.Name, model.TargetUrl, model.ToEvents()),
            cancellationToken);
        if (created is null)
        {
            ModelState.AddModelError(string.Empty, "The webhook name, event selection, or target URL is invalid, blocked, or duplicated.");
            return await RenderAsync(access.Address, model, cancellationToken);
        }
        return View(
            "~/Views/RepositoryIntegrations/WebhookCreated.cshtml",
            new CreatedWebhookViewModel(access.Address.NamespaceSlug, access.Address.RepositorySlug, created));
    }

    [HttpPost("/{namespaceSlug}/{project}/settings/webhooks/{subscriptionId:long}/active")]
    public async Task<IActionResult> SetActive(
        string namespaceSlug,
        string project,
        long subscriptionId,
        bool value,
        CancellationToken cancellationToken)
    {
        var access = await ResolveOwnerAsync(namespaceSlug, project, cancellationToken);
        if (access.Result is not null) return access.Result;
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return Forbid();
        return await _webhookService.SetSubscriptionActiveAsync(
            access.Address!.RepositoryId,
            subscriptionId,
            _currentUser.UserId,
            value,
            cancellationToken)
            ? Redirect(WebhooksPath(access.Address))
            : NotFound();
    }

    [HttpPost("/{namespaceSlug}/{project}/settings/webhooks/deliveries/{deliveryId}/replay")]
    public async Task<IActionResult> Replay(
        string namespaceSlug,
        string project,
        string deliveryId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveOwnerAsync(namespaceSlug, project, cancellationToken);
        if (access.Result is not null) return access.Result;
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)) return Forbid();
        return await _webhookService.ReplayDeliveryAsync(
            access.Address!.RepositoryId,
            deliveryId,
            _currentUser.UserId,
            cancellationToken) is not null
            ? Redirect(WebhooksPath(access.Address))
            : NotFound();
    }

    private async Task<IActionResult> RenderAsync(
        RepositoryAddressResolution address,
        CreateWebhookViewModel create,
        CancellationToken cancellationToken)
    {
        var subscriptionsTask = _webhookService.GetSubscriptionsAsync(address.RepositoryId, cancellationToken);
        var deliveriesTask = _webhookService.GetDeliveriesAsync(address.RepositoryId, cancellationToken: cancellationToken);
        await Task.WhenAll(subscriptionsTask, deliveriesTask);
        return View("~/Views/RepositoryIntegrations/Webhooks.cshtml", new RepositoryWebhooksViewModel
        {
            NamespaceSlug = address.NamespaceSlug,
            RepositoryName = address.RepositorySlug,
            Subscriptions = await subscriptionsTask,
            Deliveries = await deliveriesTask,
            Create = create
        });
    }

    private async Task<(RepositoryAddressResolution? Address, IActionResult? Result)> ResolveOwnerAsync(
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        RepositoryAddressResolution? address;
        try
        {
            address = await _addressResolver.ResolveAsync(namespaceSlug, project, cancellationToken);
        }
        catch (ArgumentException)
        {
            return (null, NotFound());
        }
        if (address is null || address.UsedAlias) return (null, NotFound());
        var authorized = await _authorizationService.AuthorizeAsync(
            User,
            new RepositoryAuthorizationResource(address.RepositoryId),
            AuthorizationPolicies.RepositoryOwner);
        return authorized.Succeeded ? (address, null) : (null, Forbid());
    }

    private static string WebhooksPath(RepositoryAddressResolution address) =>
        $"{address.CanonicalPath}/settings/webhooks";
}
