using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Git;
using GitCandy.Models;
using GitCandy.PullRequests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitCandy.Controllers;

[AutoValidateAntiforgeryToken]
[Route("{namespaceSlug}/{project}/pulls")]
public sealed class PullRequestController(
    IRepositoryAddressResolver addressResolver,
    IPullRequestService pullRequestService,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser,
    IRepositoryBrowserService repositoryBrowserService,
    IGitServiceFactory gitServiceFactory,
    IOptions<GitCandyApplicationOptions> applicationOptions) : CandyControllerBase
{
    private readonly IRepositoryAddressResolver _addressResolver = addressResolver;
    private readonly IPullRequestService _pullRequestService = pullRequestService;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IRepositoryBrowserService _repositoryBrowserService = repositoryBrowserService;
    private readonly IGitServiceFactory _gitServiceFactory = gitServiceFactory;
    private readonly GitCandyApplicationOptions _applicationOptions = applicationOptions.Value;

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

        await _pullRequestService.RefreshPullRequestAsync(
            access.Value.Address.RepositoryId,
            number,
            cancellationToken);

        var pullRequest = await _pullRequestService.GetPullRequestAsync(
            access.Value.Address.RepositoryId,
            number,
            cancellationToken);
        if (pullRequest is null)
        {
            return NotFound();
        }

        var reviewOverview = await _pullRequestService.GetReviewOverviewAsync(
            access.Value.Address.RepositoryId,
            number,
            cancellationToken);
        if (reviewOverview is null)
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
            CanChangeState = access.Value.IsOwner || ownsPullRequest,
            ReviewOverview = reviewOverview,
            CanManageReviewers = access.Value.IsOwner || ownsPullRequest,
            CanSubmitReview = _currentUser.IsAuthenticated
                && reviewOverview.Reviewers.Any(item => string.Equals(item.UserId, _currentUser.UserId, StringComparison.Ordinal)),
            IsOwner = access.Value.IsOwner,
            CurrentUserId = _currentUser.UserId
        });
    }

    [HttpGet("{number:long}/commits")]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Commits(
        string namespaceSlug,
        string project,
        long number,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var model = await GetChangesViewModelAsync(
            namespaceSlug,
            project,
            number,
            Math.Max(1, page),
            includeFiles: false,
            cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet("{number:long}/files")]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Files(
        string namespaceSlug,
        string project,
        long number,
        CancellationToken cancellationToken = default)
    {
        var model = await GetChangesViewModelAsync(
            namespaceSlug,
            project,
            number,
            commitPage: 1,
            includeFiles: true,
            cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpGet("{number:long}/commits/{sha:length(40)}")]
    [AllowAnonymous]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Commit(
        string namespaceSlug,
        string project,
        long number,
        string sha,
        CancellationToken cancellationToken = default)
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

        var commit = _repositoryBrowserService.ReadCommit(
            _gitServiceFactory.Create(access.Value.Address.StorageName),
            sha,
            cancellationToken);
        return commit is null
            ? NotFound()
            : View(new PullRequestCommitViewModel
            {
                Repository = access.Value.Address,
                PullRequest = pullRequest,
                Commit = commit
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

    [HttpPost("{number:long}/assignee")]
    [Authorize]
    public Task<IActionResult> Assignee(
        string namespaceSlug,
        string project,
        long number,
        string? assigneeUserId,
        CancellationToken cancellationToken) =>
        MutateAsync(
            namespaceSlug,
            project,
            number,
            (repositoryId, userId, isOwner) => _pullRequestService.SetAssigneeAsync(
                repositoryId,
                number,
                userId,
                isOwner,
                assigneeUserId,
                cancellationToken),
            cancellationToken);

    [HttpPost("{number:long}/reviewers")]
    [Authorize]
    public Task<IActionResult> RequestReview(
        string namespaceSlug,
        string project,
        long number,
        string reviewerUserId,
        CancellationToken cancellationToken) =>
        MutateAsync(
            namespaceSlug,
            project,
            number,
            (repositoryId, userId, isOwner) => _pullRequestService.RequestReviewAsync(
                repositoryId,
                number,
                userId,
                isOwner,
                reviewerUserId,
                cancellationToken),
            cancellationToken);

    [HttpPost("{number:long}/reviews")]
    [Authorize]
    public async Task<IActionResult> SubmitReview(
        string namespaceSlug,
        string project,
        long number,
        PullRequestReviewFormViewModel model,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null || string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return NotFound();
        }

        var result = ModelState.IsValid
            ? await _pullRequestService.SubmitReviewAsync(
                access.Value.Address.RepositoryId,
                number,
                _currentUser.UserId,
                new SubmitPullRequestReviewCommand(model.State, model.Body),
                cancellationToken)
            : PullRequestMutationResult.Invalid;
        if (result != PullRequestMutationResult.Succeeded)
        {
            TempData["PullRequestError"] = MutationMessage(result);
        }

        return RedirectToAction(nameof(Detail), new { namespaceSlug, project, number });
    }

    [HttpPost("{number:long}/reviews/{reviewId:long}/dismiss")]
    [Authorize]
    public async Task<IActionResult> DismissReview(
        string namespaceSlug,
        string project,
        long number,
        long reviewId,
        PullRequestReviewDismissFormViewModel model,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null || string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            return NotFound();
        }

        var result = ModelState.IsValid
            ? await _pullRequestService.DismissReviewAsync(
                access.Value.Address.RepositoryId,
                number,
                reviewId,
                _currentUser.UserId,
                access.Value.IsOwner,
                model.Reason,
                cancellationToken)
            : PullRequestMutationResult.Invalid;
        if (result != PullRequestMutationResult.Succeeded)
        {
            TempData["PullRequestError"] = MutationMessage(result);
        }

        return RedirectToAction(nameof(Detail), new { namespaceSlug, project, number });
    }

    [HttpPost("{number:long}/review-threads")]
    [Authorize]
    public async Task<IActionResult> AddReviewThread(string namespaceSlug, string project, long number, PullRequestReviewThreadFormViewModel model, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null || string.IsNullOrWhiteSpace(_currentUser.UserId)) return NotFound();
        var result = ModelState.IsValid
            ? await _pullRequestService.AddReviewThreadAsync(access.Value.Address.RepositoryId, number, _currentUser.UserId,
                new CreatePullRequestReviewThreadCommand(
                    model.Side == PullRequestDiffSide.Old && !string.IsNullOrWhiteSpace(model.OldPath) ? model.OldPath : model.Path,
                    model.Side,
                    model.StartLine,
                    model.EndLine,
                    model.Body), cancellationToken)
            : PullRequestMutationResult.Invalid;
        SetReviewMutationError(result);
        return RedirectToAction(nameof(Files), new { namespaceSlug, project, number });
    }

    [HttpPost("{number:long}/review-threads/{threadId:long}/replies")]
    [Authorize]
    public async Task<IActionResult> AddReviewReply(string namespaceSlug, string project, long number, long threadId, PullRequestReviewReplyFormViewModel model, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null || string.IsNullOrWhiteSpace(_currentUser.UserId)) return NotFound();
        var result = ModelState.IsValid
            ? await _pullRequestService.AddReviewReplyAsync(access.Value.Address.RepositoryId, number, threadId, _currentUser.UserId, model.Body, cancellationToken)
            : PullRequestMutationResult.Invalid;
        SetReviewMutationError(result);
        return RedirectToAction(nameof(Files), new { namespaceSlug, project, number });
    }

    [HttpPost("{number:long}/review-threads/{threadId:long}/resolved")]
    [Authorize]
    public async Task<IActionResult> SetReviewThreadResolved(string namespaceSlug, string project, long number, long threadId, bool resolved, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null || string.IsNullOrWhiteSpace(_currentUser.UserId)) return NotFound();
        var result = await _pullRequestService.SetReviewThreadResolvedAsync(
            access.Value.Address.RepositoryId, number, threadId, _currentUser.UserId, access.Value.IsOwner, resolved, cancellationToken);
        SetReviewMutationError(result);
        return RedirectToAction(nameof(Files), new { namespaceSlug, project, number });
    }

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

    private async Task<PullRequestChangesViewModel?> GetChangesViewModelAsync(
        string namespaceSlug,
        string project,
        long number,
        int commitPage,
        bool includeFiles,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, cancellationToken);
        if (access is null)
        {
            return null;
        }

        await _pullRequestService.RefreshPullRequestAsync(access.Value.Address.RepositoryId, number, cancellationToken);

        var pullRequest = await _pullRequestService.GetPullRequestAsync(
            access.Value.Address.RepositoryId,
            number,
            cancellationToken);
        if (pullRequest is null)
        {
            return null;
        }

        var changes = await _pullRequestService.GetPullRequestChangesAsync(
            access.Value.Address.RepositoryId,
            number,
            commitPage,
            Math.Clamp(_applicationOptions.NumberOfCommitsPerPage, 1, 100),
            includeFiles,
            cancellationToken);
        return changes is null
            ? null
            : new PullRequestChangesViewModel
            {
                Repository = access.Value.Address,
                PullRequest = pullRequest,
                Changes = changes,
                ReviewThreads = includeFiles
                    ? await _pullRequestService.GetReviewThreadsAsync(access.Value.Address.RepositoryId, number, cancellationToken)
                    : [],
                CanReview = includeFiles && _currentUser.IsAuthenticated,
                IsOwner = access.Value.IsOwner,
                CurrentUserId = _currentUser.UserId
            };
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

    private void SetReviewMutationError(PullRequestMutationResult result)
    {
        if (result != PullRequestMutationResult.Succeeded) TempData["PullRequestError"] = MutationMessage(result);
    }

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
