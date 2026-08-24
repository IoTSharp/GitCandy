using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Models;
using GitCandy.Remotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public sealed class RepositoryMirrorController(
    IRepositoryAddressResolver addressResolver,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser,
    IRemoteConnectionService connectionService,
    IRemoteMirrorService mirrorService,
    IRemoteMirrorManagementService managementService,
    IRemoteMirrorJobQueue jobQueue) : Controller
{
    [HttpGet("/{namespaceSlug}/{project}/settings/mirrors", Name = "canonical-repository-mirrors")]
    public async Task<IActionResult> Index(
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var access = await ResolveOwnerAsync(namespaceSlug, project, cancellationToken);
        return access.Result ?? View(await CreateViewModelAsync(
            access.Address!,
            new RepositoryMirrorFormViewModel(),
            cancellationToken));
    }

    [HttpPost("/{namespaceSlug}/{project}/settings/mirrors")]
    public async Task<IActionResult> Create(
        string namespaceSlug,
        string project,
        [Bind(Prefix = "Form")] RepositoryMirrorFormViewModel model,
        CancellationToken cancellationToken)
    {
        var access = await ResolveOwnerAsync(namespaceSlug, project, cancellationToken);
        if (access.Result is not null)
        {
            return access.Result;
        }
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return Forbid();
        }
        if (model.DivergencePolicy == RemoteMirrorDivergencePolicy.OverwriteTarget && !model.ConfirmForce)
        {
            ModelState.AddModelError("Form.ConfirmForce", "Force overwrite requires explicit confirmation.");
        }

        var connections = await connectionService.GetForUserAsync(currentUser.UserId, cancellationToken);
        var connection = connections.SingleOrDefault(item => item.Id == model.ConnectionId && item.IsEnabled);
        if (connection is null)
        {
            ModelState.AddModelError("Form.ConnectionId", "The selected remote account is unavailable.");
        }
        if (!Uri.TryCreate(model.RemoteWebUrl, UriKind.Absolute, out var webUrl))
        {
            ModelState.AddModelError("Form.RemoteWebUrl", "A valid absolute remote URL is required.");
        }
        if (!ModelState.IsValid)
        {
            return View("Index", await CreateViewModelAsync(access.Address!, model, cancellationToken));
        }

        var profile = new RemoteRepositoryProfile(
            new RemoteRepositoryIdentity(
                connection!.Provider,
                connection.ServerUrl.AbsoluteUri,
                model.RemoteRepositoryId),
            model.RemoteOwner.Trim(),
            model.RemoteRepositoryName.Trim(),
            $"{model.RemoteOwner.Trim()}/{model.RemoteRepositoryName.Trim()}",
            webUrl!,
            true,
            null);
        var pull = model.Direction == RemoteMirrorDirection.Pull;
        var result = await mirrorService.RegisterAsync(
            currentUser.UserId,
            new RemoteMirrorRegistration(
                access.Address!.RepositoryId,
                connection.Id,
                profile,
                model.Direction,
                model.RefFilterKind,
                model.RefFilterPattern,
                pull ? model.ScheduleIntervalMinutes : null,
                pull ? "UTC" : null,
                pull,
                model.DivergencePolicy,
                model.Prune),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, $"The mirror could not be registered ({result.ErrorCode}).");
            return View("Index", await CreateViewModelAsync(access.Address, model, cancellationToken));
        }

        TempData["Message"] = "The mirror was registered and its initial synchronization was queued.";
        return Redirect(MirrorsPath(access.Address));
    }

    [HttpPost("/{namespaceSlug}/{project}/settings/mirrors/{mirrorId:long}/pause")]
    public Task<IActionResult> Pause(
        string namespaceSlug,
        string project,
        long mirrorId,
        bool value,
        CancellationToken cancellationToken) =>
        ApplyMirrorActionAsync(
            namespaceSlug,
            project,
            (repositoryId, userId) => managementService.SetPausedAsync(
                repositoryId, mirrorId, userId, value, cancellationToken),
            cancellationToken);

    [HttpPost("/{namespaceSlug}/{project}/settings/mirrors/{mirrorId:long}/sync")]
    public Task<IActionResult> Sync(
        string namespaceSlug,
        string project,
        long mirrorId,
        CancellationToken cancellationToken) =>
        ApplyMirrorActionAsync(
            namespaceSlug,
            project,
            (repositoryId, userId) => managementService.EnqueueAsync(
                repositoryId, mirrorId, userId, cancellationToken),
            cancellationToken);

    [HttpPost("/{namespaceSlug}/{project}/settings/mirrors/jobs/{jobId:long}/cancel")]
    public Task<IActionResult> Cancel(
        string namespaceSlug,
        string project,
        long jobId,
        CancellationToken cancellationToken) =>
        ApplyMirrorActionAsync(
            namespaceSlug,
            project,
            (repositoryId, userId) => managementService.CancelAsync(
                repositoryId, jobId, userId, cancellationToken),
            cancellationToken);

    [HttpPost("/{namespaceSlug}/{project}/settings/mirrors/jobs/{jobId:long}/retry")]
    public Task<IActionResult> Retry(
        string namespaceSlug,
        string project,
        long jobId,
        CancellationToken cancellationToken) =>
        ApplyMirrorActionAsync(
            namespaceSlug,
            project,
            (repositoryId, userId) => managementService.RetryAsync(
                repositoryId, jobId, userId, cancellationToken),
            cancellationToken);

    [HttpPost("/{namespaceSlug}/{project}/settings/mirrors/{mirrorId:long}/delete")]
    public Task<IActionResult> Delete(
        string namespaceSlug,
        string project,
        long mirrorId,
        CancellationToken cancellationToken) =>
        ApplyMirrorActionAsync(
            namespaceSlug,
            project,
            (repositoryId, userId) => managementService.DeleteAsync(
                repositoryId, mirrorId, userId, cancellationToken),
            cancellationToken);

    private async Task<IActionResult> ApplyMirrorActionAsync(
        string namespaceSlug,
        string project,
        Func<long, string, Task<bool>> operation,
        CancellationToken cancellationToken)
    {
        var access = await ResolveOwnerAsync(namespaceSlug, project, cancellationToken);
        if (access.Result is not null)
        {
            return access.Result;
        }
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return Forbid();
        }
        return await operation(access.Address!.RepositoryId, currentUser.UserId)
            ? Redirect(MirrorsPath(access.Address))
            : NotFound();
    }

    private async Task<RepositoryMirrorIndexViewModel> CreateViewModelAsync(
        RepositoryAddressResolution address,
        RepositoryMirrorFormViewModel form,
        CancellationToken cancellationToken)
    {
        var mirrorsTask = managementService.GetForRepositoryAsync(address.RepositoryId, cancellationToken);
        var jobsTask = jobQueue.GetForRepositoryAsync(address.RepositoryId, 100, cancellationToken);
        var connectionsTask = connectionService.GetForUserAsync(currentUser.UserId!, cancellationToken);
        await Task.WhenAll(mirrorsTask, jobsTask, connectionsTask);
        return new RepositoryMirrorIndexViewModel
        {
            NamespaceSlug = address.NamespaceSlug,
            RepositoryName = address.RepositorySlug,
            Mirrors = await mirrorsTask,
            Jobs = await jobsTask,
            Connections = await connectionsTask,
            Form = form
        };
    }

    private async Task<(RepositoryAddressResolution? Address, IActionResult? Result)> ResolveOwnerAsync(
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
            return (null, NotFound());
        }
        if (address is null || address.UsedAlias)
        {
            return (null, NotFound());
        }
        var authorized = await authorizationService.AuthorizeAsync(
            User,
            new RepositoryAuthorizationResource(address.RepositoryId),
            AuthorizationPolicies.RepositoryOwner);
        return authorized.Succeeded ? (address, null) : (null, Forbid());
    }

    private static string MirrorsPath(RepositoryAddressResolution address) =>
        $"{address.CanonicalPath}/settings/mirrors";
}
