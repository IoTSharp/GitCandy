using GitCandy.Models;
using GitCandy.Workspace;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[AllowAnonymous]
public sealed class PublicProfileController(IWorkspaceService workspaceService) : CandyControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(string username, string? tab = null, int page = 1, CancellationToken cancellationToken = default)
    {
        if (GitCandy.Application.NamespaceSlugRules.IsReserved(username)) return NotFound();
        if (!TryParseTab(tab, out var parsedTab)) return NotFound();
        var profile = await workspaceService.GetPublicProfileAsync(username, parsedTab, page, 25, cancellationToken);
        return profile is null ? NotFound() : View(new PublicProfileViewModel(profile));
    }

    private static bool TryParseTab(string? value, out PublicProfileTab tab)
    {
        if (string.IsNullOrWhiteSpace(value)) { tab = PublicProfileTab.Repositories; return true; }
        return Enum.TryParse(value, true, out tab) && Enum.IsDefined(tab);
    }
}
