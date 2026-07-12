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
    IRepositoryAddressResolver addressResolver,
    INameManagementService nameManagementService,
    IRepositoryManagementService repositoryManagementService,
    IRepositoryLifecycleService repositoryLifecycleService,
    IRepositoryBrowserService repositoryBrowserService,
    IManagedGitRepositoryService managedGitRepositoryService,
    IGitServiceFactory gitServiceFactory,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser,
    IOptions<GitCandyApplicationOptions> applicationOptions) : CandyControllerBase
{
    private const string ResolvedRepositoryItemKey = "GitCandy.ResolvedRepository";
    private readonly IRepositoryService _repositoryService = repositoryService;
    private readonly IRepositoryAddressResolver _addressResolver = addressResolver;
    private readonly INameManagementService _nameManagementService = nameManagementService;
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
            ? View(new RepositoryFormViewModel { NamespaceSlug = _currentUser.UserName })
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

        var namespaceSlug = model.NamespaceSlug ?? _currentUser.UserName;
        if (!string.IsNullOrWhiteSpace(namespaceSlug))
        {
            var address = await _addressResolver.ResolveAsync(
                namespaceSlug,
                model.Name,
                cancellationToken);
            if (address is not null)
            {
                return Redirect(address.CanonicalPath);
            }
        }

        return RedirectToAction(nameof(Index));
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

        var repository = await _repositoryManagementService.GetRepositoryAsync(
            GetResolvedRepository().RepositoryId,
            cancellationToken);
        return repository is null
            ? NotFound()
            : View(new RepositoryFormViewModel
            {
                Name = repository.Name,
                Description = repository.Description,
                IsPrivate = repository.IsPrivate,
                AllowAnonymousRead = repository.AllowAnonymousRead,
                AllowAnonymousWrite = repository.AllowAnonymousWrite,
                DefaultBranch = TryGetDefaultBranch(GetResolvedRepository().StorageName)
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
                ? Redirect(GetResolvedRepository().CanonicalPath)
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

        var repository = await _repositoryManagementService.GetRepositoryAsync(
            GetResolvedRepository().RepositoryId,
            cancellationToken);
        return repository is null
            ? NotFound()
            : View(new RepositoryDetailsViewModel { Repository = repository, CanManage = true });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Rename(string name, CancellationToken cancellationToken)
    {
        var address = await _addressResolver.ResolveLegacyAsync(name, cancellationToken);
        if (address is null)
        {
            return NotFound();
        }

        var denied = await RequireRepositoryAsync(address.RepositoryId, AuthorizationPolicies.RepositoryOwner);
        if (denied is not null)
        {
            return denied;
        }

        var snapshot = await _nameManagementService.GetRepositorySnapshotAsync(address.RepositoryId, cancellationToken);
        return snapshot is null
            ? NotFound()
            : View("~/Views/Shared/Rename.cshtml", new NameChangeViewModel
            {
                CurrentSlug = snapshot.CurrentSlug,
                NewSlug = snapshot.CurrentSlug,
                SubjectType = NameSubjectType.Repository,
                Snapshot = snapshot
            });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Rename(NameChangeViewModel model, CancellationToken cancellationToken)
    {
        var address = await _addressResolver.ResolveLegacyAsync(model.CurrentSlug, cancellationToken);
        if (address is null)
        {
            return NotFound();
        }

        var denied = await RequireRepositoryAsync(address.RepositoryId, AuthorizationPolicies.RepositoryOwner);
        if (denied is not null)
        {
            return denied;
        }

        var snapshot = await _nameManagementService.GetRepositorySnapshotAsync(address.RepositoryId, cancellationToken);
        model.Snapshot = snapshot;
        model.SubjectType = NameSubjectType.Repository;
        if (!ModelState.IsValid || snapshot is null || string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return View("~/Views/Shared/Rename.cshtml", model);
        }

        var changeOverride = _currentUser.IsAdministrator && model.UseOverride
            ? new NameChangeOverride(model.OverrideReason ?? string.Empty, model.ConfirmOverride)
            : null;
        var result = await _nameManagementService.RenameRepositoryAsync(
            address.RepositoryId,
            model.NewSlug,
            _currentUser.UserId,
            changeOverride,
            cancellationToken);
        if (result.Status != NameChangeStatus.Succeeded)
        {
            ModelState.AddModelError(nameof(model.NewSlug), result.Status switch
            {
                NameChangeStatus.Occupied => "This repository URL is occupied by a current name or retained alias.",
                NameChangeStatus.ConfirmationRequired => "An override requires a reason and explicit confirmation.",
                _ => "The repository URL could not be changed."
            });
            return View("~/Views/Shared/Rename.cshtml", model);
        }

        return Redirect($"/{Uri.EscapeDataString(address.NamespaceSlug)}/{Uri.EscapeDataString(result.CanonicalSlug!)}");
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

    [HttpGet("/{namespaceSlug}/{project}/tree/{**path}", Name = "canonical-repository-tree")]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Tree(
        string? path,
        string? revision,
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(
            namespaceSlug,
            project,
            AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var tree = _repositoryBrowserService.ReadTree(
                _gitServiceFactory.Create(GetResolvedRepository().StorageName),
                revision,
                path,
                cancellationToken);
            if (tree is null && !IsEmptyRepository(GetResolvedRepository().StorageName))
            {
                return NotFound();
            }

            SetCanonicalAddressViewData();
            return View(new RepositoryTreeViewModel
            {
                RepositoryName = GetResolvedRepository().RepositorySlug,
                Tree = tree
            });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet("/{namespaceSlug}/{project}/blob/{**path}", Name = "canonical-repository-blob")]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Blob(
        string path,
        string? revision,
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var blob = _repositoryBrowserService.ReadBlob(
                _gitServiceFactory.Create(GetResolvedRepository().StorageName),
                revision,
                path,
                cancellationToken);
            return blob is null
                ? NotFound()
                : RenderCanonicalView(new RepositoryBlobViewModel
                {
                    RepositoryName = GetResolvedRepository().RepositorySlug,
                    Blob = blob
                });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet("/{namespaceSlug}/{project}/raw/{**path}", Name = "canonical-repository-raw")]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Raw(
        string path,
        string? revision,
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var context = _gitServiceFactory.Create(GetResolvedRepository().StorageName);
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

    [HttpGet("/{namespaceSlug}/{project}/commits", Name = "canonical-repository-commits")]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Commits(
        string? revision,
        string namespaceSlug,
        string project,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var denied = await RequireRepositoryAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var commits = _repositoryBrowserService.ReadCommits(
                _gitServiceFactory.Create(GetResolvedRepository().StorageName),
                revision,
                page,
                _applicationOptions.NumberOfCommitsPerPage,
                cancellationToken);
            return commits is null
                ? NotFound()
                : RenderCanonicalView(new RepositoryCommitsViewModel
                {
                    RepositoryName = GetResolvedRepository().RepositorySlug,
                    Page = commits
                });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet("/{namespaceSlug}/{project}/commit/{path}", Name = "canonical-repository-commit")]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Commit(
        string path,
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var commit = _repositoryBrowserService.ReadCommit(
                _gitServiceFactory.Create(GetResolvedRepository().StorageName),
                path,
                cancellationToken);
            return commit is null
                ? NotFound()
                : RenderCanonicalView(new RepositoryCommitViewModel
                {
                    RepositoryName = GetResolvedRepository().RepositorySlug,
                    Commit = commit
                });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet("/{namespaceSlug}/{project}/blame/{**path}", Name = "canonical-repository-blame")]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Blame(
        string path,
        string? revision,
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        try
        {
            var blame = _repositoryBrowserService.ReadBlame(
                _gitServiceFactory.Create(GetResolvedRepository().StorageName),
                revision,
                path,
                cancellationToken);
            return blame is null
                ? NotFound()
                : RenderCanonicalView(new RepositoryBlameViewModel
                {
                    RepositoryName = GetResolvedRepository().RepositorySlug,
                    Blame = blame
                });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
        {
            return NotFound();
        }
    }

    [HttpGet("/{namespaceSlug}/{project}/compare", Name = "canonical-repository-compare")]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Compare(
        string? baseRevision,
        string? headRevision,
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var denied = await RequireRepositoryAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryRead);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(baseRevision) || string.IsNullOrWhiteSpace(headRevision))
        {
            SetCanonicalAddressViewData();
            return View(model: null);
        }

        try
        {
            var compare = _repositoryBrowserService.Compare(
                _gitServiceFactory.Create(GetResolvedRepository().StorageName),
                baseRevision,
                headRevision,
                cancellationToken);
            return compare is null
                ? NotFound()
                : RenderCanonicalView(new RepositoryCompareViewModel
                {
                    RepositoryName = GetResolvedRepository().RepositorySlug,
                    Compare = compare
                });
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception))
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

        var address = await _addressResolver.ResolveLegacyAsync(name, _currentUser.RequestAborted);
        if (address is null)
        {
            return NotFound();
        }

        var result = await _authorizationService.AuthorizeAsync(
            User,
            new RepositoryAuthorizationResource(address.RepositoryId),
            policy);
        if (result.Succeeded)
        {
            HttpContext.Items[ResolvedRepositoryItemKey] = address;
            return null;
        }

        return _currentUser.IsAuthenticated ? Forbid() : Challenge();
    }

    private async Task<IActionResult?> RequireRepositoryAsync(
        string namespaceSlug,
        string project,
        string policy)
    {
        RepositoryAddressResolution? address;
        try
        {
            address = await _addressResolver.ResolveAsync(
                namespaceSlug,
                project,
                _currentUser.RequestAborted);
        }
        catch (ArgumentException)
        {
            return NotFound();
        }

        if (address is null || address.UsedAlias)
        {
            return NotFound();
        }

        var result = await _authorizationService.AuthorizeAsync(
            User,
            new RepositoryAuthorizationResource(address.RepositoryId),
            policy);
        if (!result.Succeeded)
        {
            return _currentUser.IsAuthenticated ? Forbid() : Challenge();
        }

        HttpContext.Items[ResolvedRepositoryItemKey] = address;
        return null;
    }

    private IActionResult RenderCanonicalView(object model)
    {
        SetCanonicalAddressViewData();
        return View(model);
    }

    private void SetCanonicalAddressViewData()
    {
        var address = GetResolvedRepository();
        ViewData["CanonicalUrl"] = address.CanonicalPath;
        ViewData["NamespaceSlug"] = address.NamespaceSlug;
        ViewData["RepositorySlug"] = address.RepositorySlug;
    }

    private RepositoryAddressResolution GetResolvedRepository()
    {
        return HttpContext.Items.TryGetValue(ResolvedRepositoryItemKey, out var value)
            && value is RepositoryAddressResolution address
                ? address
                : throw new InvalidOperationException("The repository address was not resolved before use.");
    }

    private async Task<IActionResult?> RequireRepositoryAsync(long repositoryId, string policy)
    {
        var result = await _authorizationService.AuthorizeAsync(
            User,
            new RepositoryAuthorizationResource(repositoryId),
            policy);
        if (result.Succeeded)
        {
            return null;
        }

        return _currentUser.IsAuthenticated ? Forbid() : Challenge();
    }
}
