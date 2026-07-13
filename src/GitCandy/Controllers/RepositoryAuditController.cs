using GitCandy.Application;
using GitCandy.Audit;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[Authorize]
[Route("{namespaceSlug}/{project}/settings/audit")]
public sealed class RepositoryAuditController(
    IRepositoryAddressResolver addressResolver,
    IAuthorizationService authorizationService,
    IAuditLogService auditLogService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        RepositoryAddressResolution? address;
        try
        {
            address = await addressResolver.ResolveAsync(namespaceSlug, project, cancellationToken);
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
        if (address is null || address.UsedAlias) return NotFound();
        var authorized = await authorizationService.AuthorizeAsync(
            User,
            new RepositoryAuthorizationResource(address.RepositoryId),
            AuthorizationPolicies.RepositoryOwner);
        if (!authorized.Succeeded) return Forbid();
        return View(new RepositoryAuditViewModel(
            address.NamespaceSlug,
            address.RepositorySlug,
            await auditLogService.GetRepositoryEventsAsync(address.RepositoryId, cancellationToken: cancellationToken)));
    }
}
