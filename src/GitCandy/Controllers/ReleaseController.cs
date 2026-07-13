using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Models;
using GitCandy.Releases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[AutoValidateAntiforgeryToken]
[Route("{namespaceSlug}/{project}/releases")]
public sealed class ReleaseController(
    IRepositoryAddressResolver addressResolver,
    IAuthorizationService authorizationService,
    IReleaseService releaseService,
    ICurrentUser currentUser) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(namespaceSlug, project, cancellationToken);
        if (access is null || !access.Value.CanRead) return NotFound();
        return View(new ReleaseIndexViewModel(
            access.Value.Address,
            await releaseService.GetReleasesAsync(
                access.Value.Address.RepositoryId,
                access.Value.CanWrite,
                cancellationToken),
            access.Value.CanWrite));
    }

    [HttpGet("new")]
    [Authorize]
    public async Task<IActionResult> Create(
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(namespaceSlug, project, cancellationToken);
        return access is { CanWrite: true } ? View(new ReleaseFormViewModel()) : Forbid();
    }

    [HttpPost("new")]
    [Authorize]
    public async Task<IActionResult> Create(
        string namespaceSlug,
        string project,
        ReleaseFormViewModel model,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(namespaceSlug, project, cancellationToken);
        if (access is not { CanWrite: true } || string.IsNullOrWhiteSpace(currentUser.UserId)) return Forbid();
        if (ModelState.IsValid)
        {
            var created = await releaseService.CreateAsync(
                access.Value.Address.RepositoryId,
                currentUser.UserId,
                new CreateRelease(model.TagName, model.Name, model.Body, model.IsDraft),
                cancellationToken);
            if (created is not null)
            {
                return RedirectToAction(nameof(Detail), new { namespaceSlug, project, releaseId = created.Id });
            }
            ModelState.AddModelError(string.Empty, "The tag does not exist or already has a release.");
        }
        return View(model);
    }

    [HttpGet("{releaseId:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> Detail(
        string namespaceSlug,
        string project,
        long releaseId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(namespaceSlug, project, cancellationToken);
        if (access is null || !access.Value.CanRead) return NotFound();
        var release = await releaseService.GetReleaseAsync(
            access.Value.Address.RepositoryId,
            releaseId,
            access.Value.CanWrite,
            cancellationToken);
        return release is null ? NotFound() : View(new ReleaseDetailViewModel(access.Value.Address, release, access.Value.CanWrite));
    }

    [HttpPost("{releaseId:long}/assets")]
    [Authorize]
    [RequestFormLimits(MultipartBodyLengthLimit = 4_294_967_296)]
    public async Task<IActionResult> AddAsset(
        string namespaceSlug,
        string project,
        long releaseId,
        [Bind(Prefix = "Asset")] ReleaseAssetFormViewModel model,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(namespaceSlug, project, cancellationToken);
        if (access is not { CanWrite: true } || string.IsNullOrWhiteSpace(currentUser.UserId)) return Forbid();
        if (model.File is null || model.File.Length <= 0) return BadRequest();
        await using var content = model.File.OpenReadStream();
        var asset = await releaseService.AddAssetAsync(
            access.Value.Address.RepositoryId,
            releaseId,
            currentUser.UserId,
            model.File.FileName,
            model.File.ContentType,
            content,
            model.File.Length,
            cancellationToken);
        return asset is null ? BadRequest() : RedirectToAction(nameof(Detail), new { namespaceSlug, project, releaseId });
    }

    [HttpGet("assets/{assetId}/download")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(
        string namespaceSlug,
        string project,
        string assetId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(namespaceSlug, project, cancellationToken);
        if (access is null || !access.Value.CanRead) return NotFound();
        var download = await releaseService.OpenAssetAsync(
            access.Value.Address.RepositoryId,
            assetId,
            access.Value.CanWrite,
            cancellationToken);
        return download is null
            ? NotFound()
            : File(download.Content, download.Asset.ContentType, download.Asset.FileName, enableRangeProcessing: true);
    }

    [HttpPost("assets/{assetId}/delete")]
    [Authorize]
    public async Task<IActionResult> DeleteAsset(
        string namespaceSlug,
        string project,
        string assetId,
        long releaseId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(namespaceSlug, project, cancellationToken);
        if (access is not { CanWrite: true } || string.IsNullOrWhiteSpace(currentUser.UserId)) return Forbid();
        return await releaseService.DeleteAssetAsync(
            access.Value.Address.RepositoryId,
            assetId,
            currentUser.UserId,
            cancellationToken)
            ? RedirectToAction(nameof(Detail), new { namespaceSlug, project, releaseId })
            : NotFound();
    }

    private async Task<(RepositoryAddressResolution Address, bool CanRead, bool CanWrite)?> ResolveAsync(
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
            return null;
        }
        if (address is null || address.UsedAlias) return null;
        var resource = new RepositoryAuthorizationResource(address.RepositoryId);
        var read = await authorizationService.AuthorizeAsync(User, resource, AuthorizationPolicies.RepositoryRead);
        if (!read.Succeeded) return (address, false, false);
        var write = await authorizationService.AuthorizeAsync(User, resource, AuthorizationPolicies.RepositoryWrite);
        return (address, true, write.Succeeded);
    }
}
