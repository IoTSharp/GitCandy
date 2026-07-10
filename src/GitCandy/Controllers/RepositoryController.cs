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
public sealed class RepositoryController(
    IRepositoryService repositoryService,
    IRepositoryManagementService repositoryManagementService,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser,
    IOptions<GitCandyApplicationOptions> applicationOptions) : CandyControllerBase
{
    private readonly IRepositoryService _repositoryService = repositoryService;
    private readonly IRepositoryManagementService _repositoryManagementService = repositoryManagementService;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly GitCandyApplicationOptions _applicationOptions = applicationOptions.Value;

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var repositories = await _repositoryService.GetVisibleRepositoriesAsync(
            _currentUser.UserId,
            _currentUser.IsAdministrator,
            cancellationToken);
        return View(new RepositoryIndexViewModel
        {
            Repositories = repositories,
            CanCreateRepository = _currentUser.IsAuthenticated
                && (_applicationOptions.AllowRepositoryCreation || _currentUser.IsAdministrator)
        });
    }

    [HttpGet]
    [Authorize]
    public IActionResult Create()
    {
        return CanCreateRepository()
            ? View(new RepositoryFormViewModel())
            : Forbid();
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(
        RepositoryFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!CanCreateRepository())
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (string.IsNullOrWhiteSpace(_currentUser.UserId)
            || !await _repositoryManagementService.CreateRepositoryAsync(
                model.ToCommand(),
                _currentUser.UserId,
                cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Name), "A repository with this name already exists.");
            return View(model);
        }

        return RedirectToAction(nameof(Detail), new { name = model.Name });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Detail(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        var repository = await _repositoryManagementService.GetRepositoryAsync(name, cancellationToken);
        if (repository is null)
        {
            return NotFound();
        }

        var canManage = (await _authorizationService.AuthorizeAsync(
            User,
            new RepositoryAuthorizationResource(name),
            AuthorizationPolicies.RepositoryOwner)).Succeeded;
        return View(new RepositoryDetailsViewModel { Repository = repository, CanManage = canManage });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Edit(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryOwner);
        if (denied is not null)
        {
            return denied;
        }

        var repository = await _repositoryManagementService.GetRepositoryAsync(name, cancellationToken);
        return repository is null
            ? NotFound()
            : View(new RepositoryFormViewModel
            {
                Name = repository.Name,
                Description = repository.Description,
                IsPrivate = repository.IsPrivate,
                AllowAnonymousRead = repository.AllowAnonymousRead,
                AllowAnonymousWrite = repository.AllowAnonymousWrite
            });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Edit(
        string name,
        RepositoryFormViewModel model,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryOwner);
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

        return await _repositoryManagementService.UpdateRepositoryAsync(
            name,
            model.ToCommand(),
            cancellationToken)
                ? RedirectToAction(nameof(Detail), new { name })
                : NotFound();
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Coop(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryOwner);
        if (denied is not null)
        {
            return denied;
        }

        var repository = await _repositoryManagementService.GetRepositoryAsync(name, cancellationToken);
        return repository is null
            ? NotFound()
            : View(new RepositoryDetailsViewModel { Repository = repository, CanManage = true });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> ChooseUser(
        RepositoryUserRoleCommand command,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(command.Name, AuthorizationPolicies.RepositoryOwner);
        if (denied is not null)
        {
            return denied;
        }

        if (!_currentUser.IsAdministrator
            && string.Equals(command.User, _currentUser.UserName, StringComparison.OrdinalIgnoreCase)
            && (command.Act.Equals("del", StringComparison.OrdinalIgnoreCase)
                || (command.Act.Equals("owner", StringComparison.OrdinalIgnoreCase) && !command.Value)))
        {
            return BadRequest("Repository owners cannot remove their own owner access.");
        }

        var action = command.Act.ToLowerInvariant() switch
        {
            "add" => RepositoryUserRoleAction.Add,
            "del" => RepositoryUserRoleAction.Remove,
            "read" => RepositoryUserRoleAction.SetRead,
            "write" => RepositoryUserRoleAction.SetWrite,
            "owner" => RepositoryUserRoleAction.SetOwner,
            _ => (RepositoryUserRoleAction?)null
        };
        return action is not null
            && await _repositoryManagementService.SetUserRoleAsync(
                command.Name,
                command.User,
                action.Value,
                command.Value,
                cancellationToken)
                ? Json("success")
                : BadRequest("Unable to update the repository user role.");
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> ChooseTeam(
        RepositoryTeamRoleCommand command,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(command.Name, AuthorizationPolicies.RepositoryOwner);
        if (denied is not null)
        {
            return denied;
        }

        var action = command.Act.ToLowerInvariant() switch
        {
            "add" => RepositoryTeamRoleAction.Add,
            "del" => RepositoryTeamRoleAction.Remove,
            "read" => RepositoryTeamRoleAction.SetRead,
            "write" => RepositoryTeamRoleAction.SetWrite,
            _ => (RepositoryTeamRoleAction?)null
        };
        return action is not null
            && await _repositoryManagementService.SetTeamRoleAsync(
                command.Name,
                command.Team,
                action.Value,
                command.Value,
                cancellationToken)
                ? Json("success")
                : BadRequest("Unable to update the repository team role.");
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Delete(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryOwner);
        if (denied is not null)
        {
            return denied;
        }

        return await _repositoryManagementService.GetRepositoryAsync(name, cancellationToken) is null
            ? NotFound()
            : View(model: name);
    }

    [HttpPost, ActionName(nameof(Delete))]
    [Authorize]
    public async Task<IActionResult> DeleteConfirmed(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryOwner);
        if (denied is not null)
        {
            return denied;
        }

        return await _repositoryManagementService.DeleteRepositoryAsync(name, cancellationToken)
            ? RedirectToAction(nameof(Index))
            : NotFound();
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Tree(string name, CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryRead);
        return denied ?? RedirectToAction(nameof(Detail), new { name });
    }

    private bool CanCreateRepository()
    {
        return _currentUser.IsAuthenticated
            && (_applicationOptions.AllowRepositoryCreation || _currentUser.IsAdministrator);
    }

    private async Task<IActionResult?> RequireRepositoryAsync(string name, string policy)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NotFound();
        }

        var result = await _authorizationService.AuthorizeAsync(
            User,
            new RepositoryAuthorizationResource(name),
            policy);
        if (result.Succeeded)
        {
            return null;
        }

        return _currentUser.IsAuthenticated ? Forbid() : Challenge();
    }
}
