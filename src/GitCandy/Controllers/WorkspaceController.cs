using GitCandy.Authentication;
using GitCandy.Models;
using GitCandy.Workspace;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[Route("me")]
public sealed class WorkspaceController(IWorkspaceService workspaceService, ICurrentUser currentUser) : CandyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        return View(new WorkspaceIndexViewModel(currentUser.UserName ?? "GitCandy user",
            await workspaceService.GetDashboardAsync(currentUser.UserId, currentUser.IsAdministrator, cancellationToken)));
    }

    [HttpGet("repositories")]
    public async Task<IActionResult> Repositories(CancellationToken cancellationToken) =>
        await RenderRepositoriesAsync("Your repositories", "Repositories you can read through direct or team access.", false, cancellationToken);

    [HttpGet("stars")]
    public async Task<IActionResult> Stars(CancellationToken cancellationToken) =>
        await RenderRepositoriesAsync("Your stars", "Repositories you saved for quick access.", true, cancellationToken);

    [HttpGet("packages")]
    public IActionResult Packages() => View();

    [HttpGet("teams")]
    public async Task<IActionResult> Teams(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        return View(new WorkspaceTeamsViewModel(await workspaceService.GetTeamsAsync(currentUser.UserId, cancellationToken)));
    }

    [HttpGet("settings")]
    public IActionResult Settings() => View();

    [HttpGet("/todos")]
    public async Task<IActionResult> Todos(int page = 1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        return View(new TodoIndexViewModel(await workspaceService.GetTodosAsync(currentUser.UserId, page, 25, true, cancellationToken)));
    }

    [HttpPost("/todos/{id:long}/complete")]
    public async Task<IActionResult> CompleteTodo(long id, long version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        if (!await workspaceService.CompleteTodoAsync(id, currentUser.UserId, version, cancellationToken)) return Conflict();
        return RedirectToAction(nameof(Todos));
    }

    [HttpPost("/todos/{id:long}/restore")]
    public async Task<IActionResult> RestoreTodo(long id, long version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        if (!await workspaceService.RestoreTodoAsync(id, currentUser.UserId, version, cancellationToken)) return Conflict();
        return RedirectToAction(nameof(Todos));
    }

    [HttpPost("/todos/{id:long}/snooze")]
    public async Task<IActionResult> SnoozeTodo(long id, long version, int days = 1, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        var until = DateTime.UtcNow.AddDays(Math.Clamp(days, 1, 30));
        if (!await workspaceService.SnoozeTodoAsync(id, currentUser.UserId, version, until, cancellationToken)) return Conflict();
        return RedirectToAction(nameof(Todos));
    }

    private async Task<IActionResult> RenderRepositoriesAsync(string title, string description, bool starredOnly, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        var repositories = await workspaceService.GetRepositoriesAsync(currentUser.UserId, currentUser.IsAdministrator, cancellationToken);
        if (starredOnly) repositories = repositories.Where(item => item.IsStarred).ToArray();
        return View("Repositories", new WorkspaceRepositoriesViewModel(title, description, repositories));
    }
}
