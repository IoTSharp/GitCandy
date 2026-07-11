using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Models;
using GitCandy.PullRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[AutoValidateAntiforgeryToken]
[Route("{namespaceSlug}/{project}/pulls")]
public sealed class PullRequestController(
    IRepositoryAddressResolver addressResolver,
    IPullRequestService pullRequestService,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser) : CandyControllerBase
{
    private readonly IRepositoryAddressResolver _addressResolver = addressResolver;
    private readonly IPullRequestService _pullRequestService = pullRequestService;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly ICurrentUser _currentUser = currentUser;

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(
        string namespaceSlug,
        string project,
        PullRequestState state = PullRequestState.Open,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null)
        {
            return NotFound();
        }

        var query = new PullRequestQuery(state, page);
        return View(new PullRequestIndexViewModel
        {
            Repository = access.Value.Address,
            PullRequests = await _pullRequestService.GetPullRequestsAsync(
                access.Value.Address.RepositoryId,
                query,
                cancellationToken),
            Query = query,
            CanCreate = _currentUser.IsAuthenticated && access.Value.CanWrite
        });
    }

    [HttpGet("/Repository/Pulls/{name}")]
    [AllowAnonymous]
    public async Task<IActionResult> Legacy(string name, CancellationToken cancellationToken)
    {
        var address = await _addressResolver.ResolveLegacyAsync(name, cancellationToken);
        if (address is null || !await CanReadAsync(address.RepositoryId))
        {
            return NotFound();
        }

        return RedirectToActionPermanent(
            nameof(Index),
            new { namespaceSlug = address.NamespaceSlug, project = address.RepositorySlug });
    }

    [HttpGet("new")]
    [Authorize]
    public async Task<IActionResult> Create(
        string namespaceSlug,
        string project,
        string? source,
        string? target,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null)
        {
            return NotFound();
        }

        if (!access.Value.CanWrite)
        {
            return Forbid();
        }

        var model = new PullRequestFormViewModel
        {
            SourceBranch = source ?? string.Empty,
            TargetBranch = target ?? string.Empty
        };
        await PopulateBranchesAsync(access.Value.Address.RepositoryId, model, cancellationToken);
        SelectDefaultBranches(model);
        return View(model);
    }

    [HttpPost("new")]
    [Authorize]
    public async Task<IActionResult> Create(
        string namespaceSlug,
        string project,
        PullRequestFormViewModel model,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null || string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return NotFound();
        }

        if (!access.Value.CanWrite)
        {
            return Forbid();
        }

        if (ModelState.IsValid)
        {
            try
            {
                var pullRequest = await _pullRequestService.CreatePullRequestAsync(
                    access.Value.Address.RepositoryId,
                    new CreatePullRequestCommand(
                        model.Title,
                        model.Body,
                        _currentUser.UserId,
                        model.SourceBranch,
                        model.TargetBranch,
                        model.IsDraft),
                    cancellationToken);
                return RedirectToAction(
                    nameof(Detail),
                    new { namespaceSlug, project, number = pullRequest.Number });
            }
            catch (PullRequestValidationException exception)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
            }
            catch (InvalidOperationException)
            {
                ModelState.AddModelError(string.Empty, "The repository branches could not be read.");
            }
        }

        await PopulateBranchesAsync(access.Value.Address.RepositoryId, model, cancellationToken);
        return View(model);
    }

    [HttpGet("{number:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> Detail(
        string namespaceSlug,
        string project,
        long number,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null)
        {
            return NotFound();
        }

        var pullRequest = await _pullRequestService.GetPullRequestAsync(
            access.Value.Address.RepositoryId,
            number,
            cancellationToken);
        if (pullRequest is null)
        {
            return NotFound();
        }

        var ownsPullRequest = string.Equals(
            pullRequest.AuthorUserId,
            _currentUser.UserId,
            StringComparison.Ordinal);
        return View(new PullRequestDetailViewModel
        {
            Repository = access.Value.Address,
            PullRequest = pullRequest,
            CanEdit = access.Value.IsOwner || ownsPullRequest,
            CanChangeState = access.Value.IsOwner || ownsPullRequest
        });
    }

    [HttpGet("{number:long}/edit")]
    [Authorize]
    public async Task<IActionResult> Edit(
        string namespaceSlug,
        string project,
        long number,
        CancellationToken cancellationToken)
    {
        var result = await ResolveEditableAsync(namespaceSlug, project, number, cancellationToken);
        if (result.Result is not null)
        {
            return result.Result;
        }

        var pullRequest = result.PullRequest!;
        return View(new PullRequestFormViewModel
        {
            Title = pullRequest.Title,
            Body = pullRequest.BodyMarkdown,
            SourceBranch = pullRequest.SourceBranch,
            TargetBranch = pullRequest.TargetBranch,
            IsDraft = pullRequest.IsDraft,
            Version = pullRequest.Version
        });
    }

    [HttpPost("{number:long}/edit")]
    [Authorize]
    public async Task<IActionResult> Edit(
        string namespaceSlug,
        string project,
        long number,
        PullRequestFormViewModel model,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null || string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            var result = await _pullRequestService.EditPullRequestAsync(
                access.Value.Address.RepositoryId,
                number,
                _currentUser.UserId,
                access.Value.IsOwner,
                new EditPullRequestCommand(model.Title, model.Body, model.Version),
                cancellationToken);
            if (result == PullRequestMutationResult.Succeeded)
            {
                return RedirectToAction(nameof(Detail), new { namespaceSlug, project, number });
            }

            AddMutationError(result);
        }

        return View(model);
    }

    [HttpPost("{number:long}/draft")]
    [Authorize]
    public Task<IActionResult> Draft(
        string namespaceSlug,
        string project,
        long number,
        bool isDraft,
        CancellationToken cancellationToken) =>
        MutateAsync(
            namespaceSlug,
            project,
            number,
            (repositoryId, userId, isOwner) => _pullRequestService.SetDraftAsync(
                repositoryId,
                number,
                userId,
                isOwner,
                isDraft,
                cancellationToken),
            cancellationToken);

    [HttpPost("{number:long}/state")]
    [Authorize]
    public Task<IActionResult> State(
        string namespaceSlug,
        string project,
        long number,
        PullRequestState state,
        CancellationToken cancellationToken) =>
        MutateAsync(
            namespaceSlug,
            project,
            number,
            (repositoryId, userId, isOwner) => _pullRequestService.SetStateAsync(
                repositoryId,
                number,
                userId,
                isOwner,
                state,
                cancellationToken),
            cancellationToken);

    private async Task<IActionResult> MutateAsync(
        string namespaceSlug,
        string project,
        long number,
        Func<long, string, bool, Task<PullRequestMutationResult>> mutation,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null || string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return NotFound();
        }

        var result = await mutation(
            access.Value.Address.RepositoryId,
            _currentUser.UserId,
            access.Value.IsOwner);
        if (result != PullRequestMutationResult.Succeeded)
        {
            TempData["PullRequestError"] = MutationMessage(result);
        }

        return RedirectToAction(nameof(Detail), new { namespaceSlug, project, number });
    }

    private async Task<(
        IActionResult? Result,
        PullRequestDetails? PullRequest)> ResolveEditableAsync(
        string namespaceSlug,
        string project,
        long number,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null)
        {
            return (NotFound(), null);
        }

        var pullRequest = await _pullRequestService.GetPullRequestAsync(
            access.Value.Address.RepositoryId,
            number,
            cancellationToken);
        if (pullRequest is null)
        {
            return (NotFound(), null);
        }

        if (!access.Value.IsOwner
            && !string.Equals(pullRequest.AuthorUserId, _currentUser.UserId, StringComparison.Ordinal))
        {
            return (Forbid(), null);
        }

        return (null, pullRequest);
    }

    private async Task PopulateBranchesAsync(
        long repositoryId,
        PullRequestFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.Branches = await _pullRequestService.GetBranchesAsync(repositoryId, cancellationToken);
    }

    private static void SelectDefaultBranches(PullRequestFormViewModel model)
    {
        if (model.Branches.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(model.TargetBranch))
        {
            model.TargetBranch = model.Branches.Any(branch => branch.Name == "main")
                ? "main"
                : model.Branches[0].Name;
        }

        if (string.IsNullOrWhiteSpace(model.SourceBranch))
        {
            model.SourceBranch = model.Branches
                .FirstOrDefault(branch => branch.Name != model.TargetBranch)?.Name
                ?? model.Branches[0].Name;
        }
    }

    private async Task<(
        RepositoryAddressResolution Address,
        bool CanWrite,
        bool IsOwner)?> ResolveAccessAsync(
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var address = await _addressResolver.ResolveAsync(namespaceSlug, project, cancellationToken);
        if (address is null || !await CanReadAsync(address.RepositoryId))
        {
            return null;
        }

        var resource = new RepositoryAuthorizationResource(address.RepositoryId);
        var canWrite = (await _authorizationService.AuthorizeAsync(
            User,
            resource,
            AuthorizationPolicies.RepositoryWrite)).Succeeded;
        var isOwner = (await _authorizationService.AuthorizeAsync(
            User,
            resource,
            AuthorizationPolicies.RepositoryOwner)).Succeeded;
        return (address, canWrite, isOwner);
    }

    private async Task<bool> CanReadAsync(long repositoryId) =>
        (await _authorizationService.AuthorizeAsync(
            User,
            new RepositoryAuthorizationResource(repositoryId),
            AuthorizationPolicies.RepositoryRead)).Succeeded;

    private void AddMutationError(PullRequestMutationResult result) =>
        ModelState.AddModelError(string.Empty, MutationMessage(result));

    private static string MutationMessage(PullRequestMutationResult result) => result switch
    {
        PullRequestMutationResult.Conflict => "This Pull Request changed while you were editing it. Reload and try again.",
        PullRequestMutationResult.Forbidden => "You do not have permission to perform this action.",
        PullRequestMutationResult.Duplicate => "An open Pull Request already exists for these branches.",
        PullRequestMutationResult.NoChanges => "The source branch has no commits to merge.",
        PullRequestMutationResult.BranchNotFound => "The source or target branch does not exist.",
        PullRequestMutationResult.Invalid => "The supplied Pull Request data is invalid.",
        _ => "The Pull Request could not be updated."
    };
}
