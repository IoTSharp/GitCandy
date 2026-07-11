using GitCandy.Authentication;
using GitCandy.Issues;
using GitCandy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[Route("notifications")]
public sealed class NotificationController(IIssueService issueService, ICurrentUser currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        return View(new IssueNotificationIndexViewModel
        {
            Notifications = await issueService.GetNotificationsAsync(currentUser.UserId, currentUser.IsAdministrator, cancellationToken)
        });
    }

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> Read(long id, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(currentUser.UserId)) await issueService.MarkNotificationReadAsync(id, currentUser.UserId, cancellationToken);
        return RedirectToAction(nameof(Index));
    }
}
