using GitCandy.Authentication;
using GitCandy.Models;
using GitCandy.Remotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
[Route("me/remotes")]
public sealed class RemoteConnectionController(
    IRemoteConnectionService connectionService,
    ICurrentUser currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        long? connectionId,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return Challenge();
        }

        var discovery = connectionId is long selectedId
            ? await connectionService.DiscoverRepositoriesAsync(
                currentUser.UserId,
                selectedId,
                cursor,
                cancellationToken)
            : null;
        if (connectionId is not null && discovery is null)
        {
            return NotFound();
        }

        return View(await CreateViewModelAsync(
            currentUser.UserId,
            new RemoteConnectionFormViewModel(),
            connectionId,
            discovery,
            cancellationToken));
    }

    [HttpPost("connect")]
    public async Task<IActionResult> Connect(
        [Bind(Prefix = "Form")] RemoteConnectionFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return Challenge();
        }

        var scopes = ParseScopes(model.GrantedScopes);
        if (scopes.Count == 0)
        {
            ModelState.AddModelError("Form.GrantedScopes", "At least one granted scope is required.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await CreateSafeFailureViewModelAsync(
                currentUser.UserId,
                model,
                cancellationToken));
        }

        var result = await connectionService.ConnectUserAsync(
            currentUser.UserId,
            new RemoteUserConnectionRequest(
                model.Provider,
                model.AuthenticationKind,
                new RemoteSecret(model.Secret),
                scopes),
            cancellationToken);
        if (!result.Diagnostic.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Diagnostic.Message);
            return View("Index", await CreateSafeFailureViewModelAsync(
                currentUser.UserId,
                model,
                cancellationToken));
        }

        TempData["Message"] = result.Diagnostic.Message;
        return RedirectToAction(nameof(Index), new { connectionId = result.Connection!.Id });
    }

    [HttpPost("{connectionId:long}/test")]
    public async Task<IActionResult> Test(
        long connectionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return Challenge();
        }

        var diagnostic = await connectionService.TestUserConnectionAsync(
            currentUser.UserId,
            connectionId,
            cancellationToken);
        if (diagnostic is null)
        {
            return NotFound();
        }

        TempData[diagnostic.Succeeded ? "Message" : "Error"] = diagnostic.Message;
        return RedirectToAction(nameof(Index), new { connectionId });
    }

    [HttpPost("{connectionId:long}/disconnect")]
    public async Task<IActionResult> Disconnect(
        long connectionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return Challenge();
        }

        if (!await connectionService.DisconnectUserAsync(
                currentUser.UserId,
                connectionId,
                cancellationToken))
        {
            TempData["Error"] = "The remote account could not be disconnected. Remove dependent mirrors first.";
            return RedirectToAction(nameof(Index), new { connectionId });
        }

        TempData["Message"] = "The remote account was disconnected and its credential was revoked.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<RemoteConnectionIndexViewModel> CreateSafeFailureViewModelAsync(
        string userId,
        RemoteConnectionFormViewModel attempted,
        CancellationToken cancellationToken)
    {
        ModelState.Remove("Form.Secret");
        return await CreateViewModelAsync(
            userId,
            new RemoteConnectionFormViewModel
            {
                Provider = attempted.Provider,
                AuthenticationKind = attempted.AuthenticationKind,
                GrantedScopes = attempted.GrantedScopes,
                Secret = string.Empty
            },
            null,
            null,
            cancellationToken);
    }

    private async Task<RemoteConnectionIndexViewModel> CreateViewModelAsync(
        string userId,
        RemoteConnectionFormViewModel form,
        long? selectedConnectionId,
        RemoteRepositoryDiscoveryResult? discovery,
        CancellationToken cancellationToken) => new()
    {
        Providers = connectionService.AvailableProviders,
        Connections = await connectionService.GetForUserAsync(userId, cancellationToken),
        Form = form,
        SelectedConnectionId = selectedConnectionId,
        Discovery = discovery
    };

    private static IReadOnlySet<string> ParseScopes(string? value) => new HashSet<string>(
        (value ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static scope => !string.IsNullOrWhiteSpace(scope)),
        StringComparer.Ordinal);
}
