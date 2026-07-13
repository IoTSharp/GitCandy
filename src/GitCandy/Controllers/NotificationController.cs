using GitCandy.Authentication;
using GitCandy.Models;
using GitCandy.Workspace;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[Route("notifications")]
public sealed class NotificationController(IWorkspaceService workspaceService, ICurrentUser currentUser) : Controller
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
}
