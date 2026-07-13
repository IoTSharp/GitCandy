using GitCandy.Authentication;
using GitCandy.Models;
using GitCandy.Workspace;
using GitCandy.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[Route("notifications")]
public sealed class NotificationController(
    IWorkspaceService workspaceService,
    INotificationDeliveryService deliveryService,
    ICurrentUser currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(bool unread = false, WorkspaceNotificationReason? reason = null,
        WorkspaceResourceType? resourceType = null, bool team = false, int page = 1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        var query = new WorkspaceNotificationQuery(unread, reason, resourceType, team, page, 25);
        return View(new NotificationIndexViewModel(
            await workspaceService.GetNotificationsAsync(currentUser.UserId, currentUser.IsAdministrator, query, cancellationToken), query));
    }

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> Read(long id, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(currentUser.UserId)) await workspaceService.MarkNotificationReadAsync(id, currentUser.UserId, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(currentUser.UserId)) await workspaceService.MarkAllNotificationsReadAsync(currentUser.UserId, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> Preferences(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        return View(await LoadPreferencesAsync(
            currentUser.UserId,
            new NotificationPreferenceFormViewModel(),
            cancellationToken));
    }

    [HttpPost("preferences")]
    public async Task<IActionResult> Preferences(
        [Bind(Prefix = "Edit")] NotificationPreferenceFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        var saved = ModelState.IsValid && await deliveryService.SavePreferenceAsync(
            currentUser.UserId,
            new NotificationPreferenceEdit(
                model.EventType,
                model.EmailEnabled,
                model.WebhookEnabled,
                model.WebhookUrl,
                model.WebhookSecret),
            cancellationToken);
        if (saved) return RedirectToAction(nameof(Preferences));
        ModelState.AddModelError(string.Empty,
            "The webhook URL is blocked or a secret is required when webhook delivery is first enabled.");
        model.WebhookSecret = null;
        return View(await LoadPreferencesAsync(currentUser.UserId, model, cancellationToken));
    }

    private async Task<NotificationPreferencesViewModel> LoadPreferencesAsync(
        string userId,
        NotificationPreferenceFormViewModel edit,
        CancellationToken cancellationToken)
    {
        var preferences = deliveryService.GetPreferencesAsync(userId, cancellationToken);
        var diagnostics = deliveryService.GetDiagnosticsAsync(userId, cancellationToken: cancellationToken);
        await Task.WhenAll(preferences, diagnostics);
        return new NotificationPreferencesViewModel(await preferences, await diagnostics, edit);
    }
}
