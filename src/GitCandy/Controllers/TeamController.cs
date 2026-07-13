using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitCandy.Controllers;

[AutoValidateAntiforgeryToken]
public sealed class TeamController(
    ITeamService teamService,
    INameManagementService nameManagementService,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser,
    IOptions<GitCandyApplicationOptions> applicationOptions) : CandyControllerBase
{
    private readonly ITeamService _teamService = teamService;
    private readonly INameManagementService _nameManagementService = nameManagementService;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly GitCandyApplicationOptions _applicationOptions = applicationOptions.Value;

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<IActionResult> Index(string? query, CancellationToken cancellationToken)
    {
        return View(new TeamIndexViewModel
        {
            Query = query,
            Teams = await _teamService.GetTeamsAsync(query, cancellationToken)
        });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Search(string? query, CancellationToken cancellationToken)
    {
        var teams = await _teamService.GetTeamsAsync(query, cancellationToken);
        return Json(teams.Take(10).Select(static team => team.Name));
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public IActionResult Create()
    {
        return View(new TeamFormViewModel());
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<IActionResult> Create(TeamFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !await _teamService.CreateTeamAsync(
                model.Name,
                model.DisplayName,
                model.Description,
                _currentUser.UserId,
                cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Name), "A team with this name already exists.");
            return View(model);
        }

        return RedirectToAction(nameof(Detail), new { name = model.Name });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Detail(string name, CancellationToken cancellationToken)
    {
        if (!_applicationOptions.IsPublicServer && !_currentUser.IsAuthenticated)
        {
            return Challenge();
        }

        var team = await GetTeamAsync(name, cancellationToken);
        if (team is null)
        {
            return NotFound();
        }

        var canManage = (await _authorizationService.AuthorizeAsync(
            User,
            new TeamAuthorizationResource(name),
            AuthorizationPolicies.TeamAdministrator)).Succeeded;
        return View(new TeamDetailsViewModel { Team = team, CanManage = canManage });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Edit(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireTeamAdministratorAsync(name);
        if (denied is not null)
        {
            return denied;
        }

        var team = await GetTeamAsync(name, cancellationToken);
        return team is null
            ? NotFound()
            : View(new TeamFormViewModel
            {
                Name = team.Name,
                DisplayName = team.DisplayName,
                Description = team.Description
            });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Edit(
        string name,
        TeamFormViewModel model,
        CancellationToken cancellationToken)
    {
        var denied = await RequireTeamAdministratorAsync(name);
        if (denied is not null)
        {
            return denied;
        }

        ModelState.Remove(nameof(model.Name));
        model.Name = name;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        return await _teamService.UpdateTeamAsync(name, model.DisplayName, model.Description, cancellationToken)
            ? RedirectToAction(nameof(Detail), new { name })
            : NotFound();
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Users(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireTeamAdministratorAsync(name);
        if (denied is not null)
        {
            return denied;
        }

        var team = await GetTeamAsync(name, cancellationToken);
        return team is null
            ? NotFound()
            : View(new TeamDetailsViewModel { Team = team, CanManage = true });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Rename(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireTeamAdministratorAsync(name);
        if (denied is not null)
        {
            return denied;
        }

        var snapshot = await _nameManagementService.GetNamespaceSnapshotAsync(name, cancellationToken);
        return snapshot is null
            ? NotFound()
            : View("~/Views/Shared/Rename.cshtml", new NameChangeViewModel
            {
                CurrentSlug = snapshot.CurrentSlug,
                NewSlug = snapshot.CurrentSlug,
                SubjectType = NameSubjectType.Namespace,
                Snapshot = snapshot
            });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Rename(NameChangeViewModel model, CancellationToken cancellationToken)
    {
        var denied = await RequireTeamAdministratorAsync(model.CurrentSlug);
        if (denied is not null)
        {
            return denied;
        }

        var snapshot = await _nameManagementService.GetNamespaceSnapshotAsync(model.CurrentSlug, cancellationToken);
        if (snapshot is null)
        {
            return NotFound();
        }

        model.Snapshot = snapshot;
        model.SubjectType = NameSubjectType.Namespace;
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return View("~/Views/Shared/Rename.cshtml", model);
        }

        var changeOverride = _currentUser.IsAdministrator && model.UseOverride
            ? new NameChangeOverride(model.OverrideReason ?? string.Empty, model.ConfirmOverride)
            : null;
        var result = await _nameManagementService.RenameNamespaceAsync(
            snapshot.SubjectId,
            model.NewSlug,
            _currentUser.UserId,
            changeOverride,
            cancellationToken);
        if (result.Status != NameChangeStatus.Succeeded)
        {
            ModelState.AddModelError(nameof(model.NewSlug), result.Status switch
            {
                NameChangeStatus.RateLimited => "The rolling rename limit has been reached.",
                NameChangeStatus.Reserved => "The URL slug is reserved by the application.",
                NameChangeStatus.Occupied => "The URL slug is already occupied by a current name or retained alias.",
                NameChangeStatus.ConfirmationRequired => "An override requires a reason and explicit confirmation.",
                _ => "The team URL could not be changed."
            });
            return View("~/Views/Shared/Rename.cshtml", model);
        }

        return RedirectToAction(nameof(Detail), new { name = result.CanonicalSlug });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> ChooseUser(
        TeamMemberCommand command,
        CancellationToken cancellationToken)
    {
        var denied = await RequireTeamAdministratorAsync(command.Name);
        if (denied is not null)
        {
            return denied;
        }

        if (!_currentUser.IsAdministrator
            && string.Equals(command.User, _currentUser.UserName, StringComparison.OrdinalIgnoreCase)
            && command.Act is "del" or "member")
        {
            return BadRequest("Team administrators cannot remove their own administrator access.");
        }

        var action = command.Act.ToLowerInvariant() switch
        {
            "add" => TeamMemberAction.Add,
            "del" => TeamMemberAction.Remove,
            "admin" => TeamMemberAction.MakeTeamOwner,
            "member" => TeamMemberAction.MakeMember,
            _ => (TeamMemberAction?)null
        };
        return action is not null
            && await _teamService.SetMemberAsync(
                command.Name,
                command.User,
                action.Value,
                cancellationToken)
                ? Json("success")
                : BadRequest("Unable to update the team member.");
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<IActionResult> Delete(string name, CancellationToken cancellationToken)
    {
        return await GetTeamAsync(name, cancellationToken) is null
            ? NotFound()
            : View(model: name);
    }

    [HttpPost, ActionName(nameof(Delete))]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task<IActionResult> DeleteConfirmed(string name, CancellationToken cancellationToken)
    {
        return await _teamService.DeleteTeamAsync(name, cancellationToken)
            ? RedirectToAction(nameof(Index))
            : NotFound();
    }

    private async Task<IActionResult?> RequireTeamAdministratorAsync(string teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName))
        {
            return NotFound();
        }

        var result = await _authorizationService.AuthorizeAsync(
            User,
            new TeamAuthorizationResource(teamName),
            AuthorizationPolicies.TeamAdministrator);
        return result.Succeeded ? null : Forbid();
    }

    private Task<TeamDetails?> GetTeamAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult<TeamDetails?>(null);
        }

        return _teamService.GetTeamAsync(
            name,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken);
    }
}
