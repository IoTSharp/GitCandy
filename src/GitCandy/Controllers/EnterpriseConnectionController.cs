using GitCandy.Authentication;
using GitCandy.Enterprise;
using GitCandy.Models;
using GitCandy.Teams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[Authorize]
[AutoValidateAntiforgeryToken]
public sealed class EnterpriseConnectionController(
    IEnterpriseConnectionService connectionService,
    IScimBearerService scimBearerService,
    ITeamAuthorizationService teamAuthorizationService,
    ICurrentUser currentUser) : Controller
{
    private readonly IEnterpriseConnectionService _connectionService = connectionService;
    private readonly IScimBearerService _scimBearerService = scimBearerService;
    private readonly ITeamAuthorizationService _teamAuthorizationService = teamAuthorizationService;
    private readonly ICurrentUser _currentUser = currentUser;

    [HttpGet]
    public async Task<IActionResult> Index(string teamName, CancellationToken cancellationToken)
    {
        var connections = await _connectionService.GetForTeamAsync(
            teamName,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken);
        return connections is null
            ? Forbid()
            : View(new EnterpriseConnectionIndexViewModel
            {
                TeamName = teamName,
                Connections = connections,
                CanManage = await CanManageAsync(teamName, cancellationToken)
            });
    }

    [HttpGet]
    public async Task<IActionResult> Create(string teamName, CancellationToken cancellationToken)
    {
        return await CanManageAsync(teamName, cancellationToken)
            ? View("Edit", new EnterpriseConnectionFormViewModel { TeamName = teamName })
            : Forbid();
    }

    [HttpGet]
    public async Task<IActionResult> Edit(
        string teamName,
        long id,
        CancellationToken cancellationToken)
    {
        if (!await CanManageAsync(teamName, cancellationToken))
        {
            return Forbid();
        }

        var connection = await _connectionService.GetAsync(
            teamName,
            id,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken);
        return connection is null ? NotFound() : View(EnterpriseConnectionFormViewModel.FromSummary(connection));
    }

    [HttpPost]
    public async Task<IActionResult> Save(
        EnterpriseConnectionFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Forbid();
        }

        var saved = await _connectionService.SaveAsync(
            model.TeamName,
            model.ToEdit(),
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken);
        if (saved is null)
        {
            ModelState.AddModelError(string.Empty, "The connection could not be saved. Check permissions, stable IDs, URLs, duplicate names, and secret fields.");
            return View("Edit", model);
        }

        return RedirectToAction(nameof(Index), new { teamName = model.TeamName });
    }

    [HttpPost]
    public async Task<IActionResult> Test(
        string teamName,
        long id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !await CanManageAsync(teamName, cancellationToken))
        {
            return Forbid();
        }

        var diagnostic = await _connectionService.TestAsync(
            teamName,
            id,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken);
        if (diagnostic is null)
        {
            return Forbid();
        }

        TempData[diagnostic.Succeeded ? "Message" : "Error"] = diagnostic.Message;
        return RedirectToAction(nameof(Index), new { teamName });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(
        string teamName,
        long id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !await CanManageAsync(teamName, cancellationToken))
        {
            return Forbid();
        }

        return await _connectionService.DeleteAsync(
            teamName,
            id,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken)
            ? RedirectToAction(nameof(Index), new { teamName })
            : Forbid();
    }

    [HttpPost]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> RotateScimBearer(
        string teamName,
        long id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !await CanManageAsync(teamName, cancellationToken))
        {
            return Forbid();
        }

        var created = await _scimBearerService.RotateAsync(
            id,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken);
        return created is null ? BadRequest() : View("ScimBearerCreated", created);
    }

    private Task<bool> CanManageAsync(string teamName, CancellationToken cancellationToken) =>
        _teamAuthorizationService.IsAllowedAsync(
            teamName,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            TeamPermission.ManageEnterpriseConnections,
            cancellationToken);
}
