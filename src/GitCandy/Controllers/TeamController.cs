using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Models;
using GitCandy.Teams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitCandy.Controllers;

[AutoValidateAntiforgeryToken]
public sealed class TeamController(
    ITeamService teamService,
    INameManagementService nameManagementService,
    ITeamAuthorizationService teamAuthorizationService,
    ICurrentUser currentUser,
    IOptions<GitCandyApplicationOptions> applicationOptions) : CandyControllerBase
{
    private readonly ITeamService _teamService = teamService;
    private readonly INameManagementService _nameManagementService = nameManagementService;
    private readonly ITeamAuthorizationService _teamAuthorizationService = teamAuthorizationService;
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

        return View(await CreateDetailsViewModelAsync(team, includeAudit: false, cancellationToken));
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Edit(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireTeamPermissionAsync(name, TeamPermission.RenameTeam, cancellationToken);
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
        var denied = await RequireTeamPermissionAsync(name, TeamPermission.RenameTeam, cancellationToken);
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

        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Forbid();
        }

        return await _teamService.UpdateTeamAsync(
                name,
                model.DisplayName,
                model.Description,
                _currentUser.UserId,
                _currentUser.IsAdministrator,
                cancellationToken)
            ? RedirectToAction(nameof(Detail), new { name })
            : Forbid();
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Users(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireTeamPermissionAsync(name, TeamPermission.ManageMembers, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var team = await GetTeamAsync(name, cancellationToken);
        return team is null
            ? NotFound()
            : View(await CreateDetailsViewModelAsync(team, includeAudit: true, cancellationToken));
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Rename(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireTeamPermissionAsync(name, TeamPermission.RenameTeam, cancellationToken);
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
        var denied = await RequireTeamPermissionAsync(
            model.CurrentSlug,
            TeamPermission.RenameTeam,
            cancellationToken);
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
        var denied = await RequireTeamPermissionAsync(
            command.Name,
            TeamPermission.ManageMembers,
            cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Forbid();
        }

        var action = command.Act.ToLowerInvariant() switch
        {
            "add" => TeamMemberAction.Add,
            "del" => TeamMemberAction.Remove,
            "owner" or "admin" => TeamMemberAction.MakeTeamOwner,
            "leader" => TeamMemberAction.MakeLeader,
            "deputy" => TeamMemberAction.MakeDeputyLeader,
            "member" => TeamMemberAction.MakeMember,
            _ => (TeamMemberAction?)null
        };
        return action is not null
            && await _teamService.SetMemberAsync(
                command.Name,
                command.User,
                action.Value,
                _currentUser.UserId,
                _currentUser.IsAdministrator,
                cancellationToken)
                ? Json("success")
                : BadRequest("Unable to update the team member.");
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> UpdateUsers(
        TeamMemberBatchCommand command,
        CancellationToken cancellationToken)
    {
        var denied = await RequireTeamPermissionAsync(
            command.Name,
            TeamPermission.ManageMembers,
            cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Forbid();
        }

        var changes = new List<TeamMemberChange>();
        foreach (var member in command.Members.Where(member => member.Selected))
        {
            TeamMemberAction? action = command.Operation switch
            {
                "remove" => TeamMemberAction.Remove,
                "roles" when Enum.TryParse<TeamRole>(member.Role, ignoreCase: true, out var role) => role switch
                {
                    TeamRole.TeamOwner => TeamMemberAction.MakeTeamOwner,
                    TeamRole.Leader => TeamMemberAction.MakeLeader,
                    TeamRole.DeputyLeader => TeamMemberAction.MakeDeputyLeader,
                    TeamRole.Member => TeamMemberAction.MakeMember,
                    _ => null
                },
                _ => null
            };
            if (action is null)
            {
                return BadRequest("The requested team role is invalid.");
            }

            changes.Add(new TeamMemberChange(member.User, action.Value));
        }

        var result = await _teamService.ApplyMemberChangesAsync(
            command.Name,
            changes,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error ?? "Unable to update team members.";
        }

        return RedirectToAction(nameof(Users), new { name = command.Name });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Delete(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireTeamPermissionAsync(name, TeamPermission.DeleteTeam, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        return await GetTeamAsync(name, cancellationToken) is null
            ? NotFound()
            : View(model: name);
    }

    [HttpPost, ActionName(nameof(Delete))]
    [Authorize]
    public async Task<IActionResult> DeleteConfirmed(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Forbid();
        }

        return await _teamService.DeleteTeamAsync(
                name,
                _currentUser.UserId,
                _currentUser.IsAdministrator,
                cancellationToken)
            ? RedirectToAction("Index", "Home")
            : Forbid();
    }

    private async Task<IActionResult?> RequireTeamPermissionAsync(
        string teamName,
        TeamPermission permission,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(teamName))
        {
            return NotFound();
        }

        return await _teamAuthorizationService.IsAllowedAsync(
            teamName,
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            permission,
            cancellationToken)
            ? null
            : Forbid();
    }

    private async Task<TeamDetailsViewModel> CreateDetailsViewModelAsync(
        TeamDetails team,
        bool includeAudit,
        CancellationToken cancellationToken)
    {
        var role = string.IsNullOrWhiteSpace(_currentUser.UserId)
            ? null
            : await _teamAuthorizationService.GetRoleAsync(
                team.Name,
                _currentUser.UserId,
                cancellationToken);
        bool Allows(TeamPermission permission) =>
            _currentUser.IsAdministrator
            || (role is not null && TeamRolePermissions.Allows(role.Value, permission));
        return new TeamDetailsViewModel
        {
            Team = team,
            CurrentRole = role,
            CanManageMembers = Allows(TeamPermission.ManageMembers),
            CanEdit = Allows(TeamPermission.RenameTeam),
            CanRename = Allows(TeamPermission.RenameTeam),
            CanDelete = Allows(TeamPermission.DeleteTeam),
            CanViewEnterpriseConnections = Allows(TeamPermission.ViewEnterpriseConnections),
            AuditEvents = includeAudit
                ? await _teamService.GetAuditEventsAsync(team.Name, cancellationToken: cancellationToken)
                : []
        };
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
