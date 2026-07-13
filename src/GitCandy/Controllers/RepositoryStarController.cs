using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Workspace;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public sealed class RepositoryStarController(IRepositoryAddressResolver addressResolver, IWorkspaceService workspaceService, ICurrentUser currentUser) : Controller
{
    [HttpPost("/{namespaceSlug}/{project}/star")]
    public async Task<IActionResult> Set(string namespaceSlug, string project, bool starred, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId)) return Challenge();
        var address = await addressResolver.ResolveAsync(namespaceSlug, project, cancellationToken);
        if (address is null || address.UsedAlias) return NotFound();
        if (!await workspaceService.SetStarAsync(address.RepositoryId, currentUser.UserId, starred, cancellationToken)) return Forbid();
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : Redirect(address.CanonicalPath);
    }
}
