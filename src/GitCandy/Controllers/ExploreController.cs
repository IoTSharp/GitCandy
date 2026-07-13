using GitCandy.Models;
using GitCandy.Workspace;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[AllowAnonymous]
[Route("explore")]
public sealed class ExploreController(IWorkspaceService workspaceService) : CandyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search = null, string? license = null, int page = 1, CancellationToken cancellationToken = default)
    {
        var query = new ExploreQuery(search, license, page, 25);
        return View(new ExploreViewModel(await workspaceService.ExploreAsync(query, cancellationToken), query));
    }
}
