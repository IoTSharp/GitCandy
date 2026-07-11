using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Git;
using GitCandy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitCandy.Controllers;

[AutoValidateAntiforgeryToken]
public sealed class RepositoryController(
    IRepositoryService repositoryService,
    IRepositoryManagementService repositoryManagementService,
    IRepositoryLifecycleService repositoryLifecycleService,
    IRepositoryBrowserService repositoryBrowserService,
    IManagedGitRepositoryService managedGitRepositoryService,
    IGitServiceFactory gitServiceFactory,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser,
    IOptions<GitCandyApplicationOptions> applicationOptions) : CandyControllerBase
{
    private readonly IRepositoryService _repositoryService = repositoryService;
    private readonly IRepositoryManagementService _repositoryManagementService = repositoryManagementService;
    private readonly IRepositoryLifecycleService _repositoryLifecycleService = repositoryLifecycleService;
    private readonly IRepositoryBrowserService _repositoryBrowserService = repositoryBrowserService;
    private readonly IManagedGitRepositoryService _managedGitRepositoryService = managedGitRepositoryService;
    private readonly IGitServiceFactory _gitServiceFactory = gitServiceFactory;
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

        if (model.InitializationMode is RepositoryCreationMode.Import or RepositoryCreationMode.Fork
            && string.IsNullOrWhiteSpace(model.Source))
        {
            ModelState.AddModelError(nameof(model.Source), "A source is required for import or fork.");
            return View(model);
        }

        if (model.InitializationMode == RepositoryCreationMode.Fork)
        {
            var sourceAuthorization = await _authorizationService.AuthorizeAsync(
                User,
                new RepositoryAuthorizationResource(model.Source!),
                AuthorizationPolicies.RepositoryRead);
            if (!sourceAuthorization.Succeeded)
            {
                return Forbid();
            }
        }

        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return Forbid();
        }

        var result = await _repositoryLifecycleService.CreateAsync(
            new RepositoryCreation(
                model.ToCommand(),
                _currentUser.UserId,
                model.InitializationMode,
                model.Source),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(model.Name), result.Error ?? "The repository could not be created.");
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
                AllowAnonymousWrite = repository.AllowAnonymousWrite,
                DefaultBranch = TryGetDefaultBranch(name)
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

        if (!string.IsNullOrWhiteSpace(model.DefaultBranch)
            && !await _repositoryLifecycleService.SetDefaultBranchAsync(
                name,
                model.DefaultBranch,
                cancellationToken))
        {
            ModelState.AddModelError(nameof(model.DefaultBranch), "The branch does not exist.");
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

        return await _repositoryLifecycleService.DeleteAsync(name, cancellationToken)
            ? RedirectToAction(nameof(Index))
            : NotFound();
    }

    [HttpGet]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Tree(
        string name,
        string? path,
        string? revision,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var tree = _repositoryBrowserService.ReadTree(
                _gitServiceFactory.Create(name),
                revision,
                path,
                cancellationToken);
            if (tree is null && !IsEmptyRepository(name))
            {
                return NotFound();
            }

            return View(new RepositoryTreeViewModel { RepositoryName = name, Tree = tree });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Blob(
        string name,
        string path,
        string? revision,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var blob = _repositoryBrowserService.ReadBlob(
                _gitServiceFactory.Create(name),
                revision,
                path,
                cancellationToken);
            return blob is null
                ? NotFound()
                : View(new RepositoryBlobViewModel { RepositoryName = name, Blob = blob });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Raw(
        string name,
        string path,
        string? revision,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var context = _gitServiceFactory.Create(name);
            var blob = _repositoryBrowserService.ReadBlob(context, revision, path, cancellationToken);
            if (blob is null)
            {
                return NotFound();
            }

            Response.ContentType = blob.IsBinary || blob.HasUnknownEncoding
                ? "application/octet-stream"
                : "text/plain; charset=utf-8";
            Response.ContentLength = blob.Size;
            if (blob.IsBinary || blob.HasUnknownEncoding)
            {
                Response.Headers.ContentDisposition =
                    $"attachment; filename*=UTF-8''{Uri.EscapeDataString(blob.Name)}";
            }

            await _repositoryBrowserService.CopyBlobAsync(
                context,
                revision,
                path,
                Response.Body,
                cancellationToken);
            return new EmptyResult();
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Commits(
        string name,
        string? revision,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var commits = _repositoryBrowserService.ReadCommits(
                _gitServiceFactory.Create(name),
                revision,
                page,
                _applicationOptions.NumberOfCommitsPerPage,
                cancellationToken);
            return commits is null
                ? NotFound()
                : View(new RepositoryCommitsViewModel { RepositoryName = name, Page = commits });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Commit(
        string name,
        string path,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var commit = _repositoryBrowserService.ReadCommit(
                _gitServiceFactory.Create(name),
                path,
                cancellationToken);
            return commit is null
                ? NotFound()
                : View(new RepositoryCommitViewModel { RepositoryName = name, Commit = commit });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Blame(
        string name,
        string path,
        string? revision,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var blame = _repositoryBrowserService.ReadBlame(
                _gitServiceFactory.Create(name),
                revision,
                path,
                cancellationToken);
            return blame is null
                ? NotFound()
                : View(new RepositoryBlameViewModel { RepositoryName = name, Blame = blame });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Compare(
        string name,
        string? baseRevision,
        string? headRevision,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(baseRevision) || string.IsNullOrWhiteSpace(headRevision))
        {
            return View(model: null);
        }

        try
        {
            var compare = _repositoryBrowserService.Compare(
                _gitServiceFactory.Create(name),
                baseRevision,
                headRevision,
                cancellationToken);
            return compare is null
                ? NotFound()
                : View(new RepositoryCompareViewModel { RepositoryName = name, Compare = compare });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Archive(
        string name,
        string? revision,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(name, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition =
            $"attachment; filename*=UTF-8''{Uri.EscapeDataString(name)}.zip";
        try
        {
            var result = await _repositoryBrowserService.WriteArchiveAsync(
                _gitServiceFactory.Create(name),
                revision,
                Response.Body,
                cancellationToken);
            return result is null ? NotFound() : new EmptyResult();
        }
        catch (InvalidOperationException) when (!Response.HasStarted)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception) && !Response.HasStarted)
        {
            return NotFound();
        }
    }

    private bool CanCreateRepository()
    {
        return _currentUser.IsAuthenticated
            && (_applicationOptions.AllowRepositoryCreation || _currentUser.IsAdministrator);
    }

    private string? TryGetDefaultBranch(string repositoryName)
    {
        try
        {
            var snapshot = _managedGitRepositoryService.ReadSnapshot(
                _gitServiceFactory.Create(repositoryName));
            return snapshot.HeadCanonicalName.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? snapshot.HeadCanonicalName["refs/heads/".Length..]
                : snapshot.HeadCanonicalName;
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return null;
        }
    }

    private bool IsEmptyRepository(string repositoryName)
    {
        try
        {
            return _managedGitRepositoryService.ReadSnapshot(
                _gitServiceFactory.Create(repositoryName)).HeadCommitId is null;
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return false;
        }
    }

    private static bool IsInvalidRepositoryRequest(Exception exception)
    {
        return exception is ArgumentException
            or InvalidOperationException
            or GitRepositoryNotFoundException
            or LibGit2Sharp.LibGit2SharpException;
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
