using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Issues;
using GitCandy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[AutoValidateAntiforgeryToken]
[Route("{namespaceSlug}/{project}/issues")]
public sealed class IssueController(
    IRepositoryAddressResolver addressResolver,
    IIssueService issueService,
    IIssueTemplateService templateService,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser) : CandyControllerBase
{
    private readonly IRepositoryAddressResolver _addressResolver = addressResolver;
    private readonly IIssueService _issueService = issueService;
    private readonly IIssueTemplateService _templateService = templateService;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly ICurrentUser _currentUser = currentUser;

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(string namespaceSlug, string project, IssueState state = IssueState.Open, string? author = null, string? assignee = null, string? label = null, long? milestoneId = null, int page = 1, CancellationToken cancellationToken = default)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null) return NotFound();
        var query = new IssueQuery(state, author, assignee, label, milestoneId, page);
        return View(new IssueIndexViewModel
        {
            Repository = access.Value.Address,
            Issues = await _issueService.GetIssuesAsync(access.Value.Address.RepositoryId, query, cancellationToken),
            Query = query,
            Metadata = await _issueService.GetMetadataAsync(access.Value.Address.RepositoryId, cancellationToken),
            CanCreate = _currentUser.IsAuthenticated,
            CanManage = access.Value.IsOwner
        });
    }

    [HttpGet("new")]
    [Authorize]
    public async Task<IActionResult> Create(string namespaceSlug, string project, string? template, string? title, string? body, CancellationToken cancellationToken)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null) return NotFound();
        var issueTemplate = await _templateService.GetTemplateAsync(access.Value.Address.StorageName, template, cancellationToken);
        return View(new IssueFormViewModel
        {
            Title = title ?? issueTemplate?.Title ?? string.Empty,
            Body = body ?? issueTemplate?.Body ?? string.Empty,
            Metadata = await _issueService.GetMetadataAsync(access.Value.Address.RepositoryId, cancellationToken)
        });
    }

    [HttpPost("new")]
    [Authorize]
    public async Task<IActionResult> Create(string namespaceSlug, string project, IssueFormViewModel model, CancellationToken cancellationToken)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null || string.IsNullOrWhiteSpace(_currentUser.UserId)) return NotFound();
        if (!ModelState.IsValid)
        {
            model.Metadata = await _issueService.GetMetadataAsync(access.Value.Address.RepositoryId, cancellationToken);
            return View(model);
        }
        try
        {
            var issue = await _issueService.CreateIssueAsync(access.Value.Address.RepositoryId,
                new CreateIssueCommand(model.Title, model.Body, _currentUser.UserId, model.AssigneeUserId, model.MilestoneId, model.LabelIds), cancellationToken);
            return RedirectToAction(nameof(Detail), new { namespaceSlug, project, number = issue.Number });
        }
        catch (IssueValidationException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
        catch (IssueRateLimitException exception) { ModelState.AddModelError(string.Empty, exception.Message); }
        model.Metadata = await _issueService.GetMetadataAsync(access.Value.Address.RepositoryId, cancellationToken);
        return View(model);
    }

    [HttpGet("{number:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> Detail(string namespaceSlug, string project, long number, CancellationToken cancellationToken)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null) return NotFound();
        var issue = await _issueService.GetIssueAsync(access.Value.Address.RepositoryId, number, _currentUser.UserId, cancellationToken);
        if (issue is null) return NotFound();
        return View(new IssueDetailViewModel
        {
            Repository = access.Value.Address,
            Issue = issue,
            Metadata = await _issueService.GetMetadataAsync(access.Value.Address.RepositoryId, cancellationToken),
            CanManage = access.Value.IsOwner,
            CanEdit = access.Value.IsOwner || issue.AuthorUserId == _currentUser.UserId,
            CanChangeState = access.Value.IsOwner || issue.AuthorUserId == _currentUser.UserId || issue.AssigneeUserId == _currentUser.UserId
        });
    }

    [HttpGet("{number:long}/edit")]
    [Authorize]
    public async Task<IActionResult> Edit(string namespaceSlug, string project, long number, CancellationToken cancellationToken)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null) return NotFound();
        var issue = await _issueService.GetIssueAsync(access.Value.Address.RepositoryId, number, _currentUser.UserId, cancellationToken);
        if (issue is null) return NotFound();
        if (!access.Value.IsOwner && issue.AuthorUserId != _currentUser.UserId) return Forbid();
        return View(new IssueFormViewModel { Title = issue.Title, Body = issue.BodyMarkdown, Version = issue.Version, Metadata = await _issueService.GetMetadataAsync(access.Value.Address.RepositoryId, cancellationToken) });
    }

    [HttpPost("{number:long}/edit")]
    [Authorize]
    public async Task<IActionResult> Edit(string namespaceSlug, string project, long number, IssueFormViewModel model, CancellationToken cancellationToken)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null || string.IsNullOrWhiteSpace(_currentUser.UserId)) return NotFound();
        if (ModelState.IsValid)
        {
            var result = await _issueService.EditIssueAsync(access.Value.Address.RepositoryId, number, _currentUser.UserId, access.Value.IsOwner, new EditIssueCommand(model.Title, model.Body, model.Version), cancellationToken);
            if (result == IssueMutationResult.Succeeded) return RedirectToAction(nameof(Detail), new { namespaceSlug, project, number });
            AddMutationError(result);
        }
        model.Metadata = await _issueService.GetMetadataAsync(access.Value.Address.RepositoryId, cancellationToken);
        return View(model);
    }

    [HttpPost("{number:long}/comment")]
    [Authorize]
    public Task<IActionResult> Comment(string namespaceSlug, string project, long number, string body, CancellationToken cancellationToken) =>
        MutateAsync(namespaceSlug, project, number, (repositoryId, userId, isOwner) => _issueService.AddCommentAsync(repositoryId, number, userId, isOwner, body, cancellationToken), cancellationToken);

    [HttpPost("{number:long}/state")]
    [Authorize]
    public Task<IActionResult> State(string namespaceSlug, string project, long number, IssueState state, CancellationToken cancellationToken) =>
        MutateAsync(namespaceSlug, project, number, (repositoryId, userId, isOwner) => _issueService.SetStateAsync(repositoryId, number, userId, isOwner, state, cancellationToken), cancellationToken);

    [HttpPost("{number:long}/subscription")]
    [Authorize]
    public Task<IActionResult> Subscription(string namespaceSlug, string project, long number, bool subscribed, CancellationToken cancellationToken) =>
        MutateAsync(namespaceSlug, project, number, (repositoryId, userId, _) => _issueService.SetSubscriptionAsync(repositoryId, number, userId, subscribed, cancellationToken), cancellationToken);

    [HttpPost("{number:long}/lock")]
    [Authorize]
    public Task<IActionResult> Lock(string namespaceSlug, string project, long number, bool locked, CancellationToken cancellationToken) =>
        MutateAsync(namespaceSlug, project, number, (repositoryId, userId, isOwner) => _issueService.SetLockedAsync(repositoryId, number, userId, isOwner, locked, cancellationToken), cancellationToken);

    [HttpPost("{number:long}/assignee")]
    [Authorize]
    public Task<IActionResult> Assignee(string namespaceSlug, string project, long number, string? assigneeUserId, CancellationToken cancellationToken) =>
        MutateAsync(namespaceSlug, project, number, (repositoryId, userId, isOwner) => _issueService.SetAssigneeAsync(repositoryId, number, userId, isOwner, assigneeUserId, cancellationToken), cancellationToken);

    [HttpPost("{number:long}/milestone")]
    [Authorize]
    public Task<IActionResult> Milestone(string namespaceSlug, string project, long number, long? milestoneId, CancellationToken cancellationToken) =>
        MutateAsync(namespaceSlug, project, number, (repositoryId, userId, isOwner) => _issueService.SetMilestoneAsync(repositoryId, number, userId, isOwner, milestoneId, cancellationToken), cancellationToken);

    [HttpPost("{number:long}/label")]
    [Authorize]
    public Task<IActionResult> Label(string namespaceSlug, string project, long number, long labelId, bool selected, CancellationToken cancellationToken) =>
        MutateAsync(namespaceSlug, project, number, (repositoryId, userId, isOwner) => _issueService.SetLabelAsync(repositoryId, number, userId, isOwner, labelId, selected, cancellationToken), cancellationToken);

    [HttpPost("{number:long}/comments/{commentId:long}/edit")]
    [Authorize]
    public Task<IActionResult> EditComment(string namespaceSlug, string project, long number, long commentId, string body, CancellationToken cancellationToken) =>
        MutateAsync(namespaceSlug, project, number, (repositoryId, userId, isOwner) => _issueService.EditCommentAsync(repositoryId, number, commentId, userId, isOwner, body, cancellationToken), cancellationToken);

    [HttpPost("{number:long}/comments/{commentId:long}/hide")]
    [Authorize]
    public Task<IActionResult> HideComment(string namespaceSlug, string project, long number, long commentId, CancellationToken cancellationToken) =>
        MutateAsync(namespaceSlug, project, number, (repositoryId, userId, isOwner) => _issueService.HideCommentAsync(repositoryId, number, commentId, userId, isOwner, cancellationToken), cancellationToken);

    [HttpPost("{number:long}/relation")]
    [Authorize]
    public Task<IActionResult> Relation(string namespaceSlug, string project, long number, long targetNumber, IssueRelationType relationType, CancellationToken cancellationToken) =>
        MutateAsync(namespaceSlug, project, number, (repositoryId, userId, isOwner) => _issueService.AddRelationAsync(repositoryId, number, targetNumber, userId, isOwner, relationType, cancellationToken), cancellationToken);

    [HttpGet("settings")]
    [Authorize]
    public async Task<IActionResult> Settings(string namespaceSlug, string project, CancellationToken cancellationToken)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null || !access.Value.IsOwner) return NotFound();
        return View(new IssueMetadataViewModel { Repository = access.Value.Address, Metadata = await _issueService.GetMetadataAsync(access.Value.Address.RepositoryId, cancellationToken) });
    }

    [HttpPost("settings/label")]
    [Authorize]
    public async Task<IActionResult> SaveLabel(string namespaceSlug, string project, long? labelId, string name, string color, string description, CancellationToken cancellationToken)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null || await _issueService.SaveLabelAsync(access.Value.Address.RepositoryId, labelId, name, color, description, access.Value.IsOwner, cancellationToken) is null) return NotFound();
        return RedirectToAction(nameof(Settings), new { namespaceSlug, project });
    }

    [HttpPost("settings/milestone")]
    [Authorize]
    public async Task<IActionResult> SaveMilestone(string namespaceSlug, string project, long? milestoneId, string title, string description, DateTime? dueAtUtc, CancellationToken cancellationToken)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null || await _issueService.SaveMilestoneAsync(access.Value.Address.RepositoryId, milestoneId, title, description, dueAtUtc, access.Value.IsOwner, cancellationToken) is null) return NotFound();
        return RedirectToAction(nameof(Settings), new { namespaceSlug, project });
    }

    [HttpPost("settings/label/{labelId:long}/archive")]
    [Authorize]
    public async Task<IActionResult> ArchiveLabel(string namespaceSlug, string project, long labelId, CancellationToken cancellationToken)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null || await _issueService.ArchiveLabelAsync(access.Value.Address.RepositoryId, labelId, access.Value.IsOwner, cancellationToken) != IssueMutationResult.Succeeded) return NotFound();
        return RedirectToAction(nameof(Settings), new { namespaceSlug, project });
    }

    [HttpPost("settings/milestone/{milestoneId:long}/status")]
    [Authorize]
    public async Task<IActionResult> MilestoneStatus(string namespaceSlug, string project, long milestoneId, bool closed, bool archived, CancellationToken cancellationToken)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null || await _issueService.SetMilestoneStatusAsync(access.Value.Address.RepositoryId, milestoneId, closed, archived, access.Value.IsOwner, cancellationToken) != IssueMutationResult.Succeeded) return NotFound();
        return RedirectToAction(nameof(Settings), new { namespaceSlug, project });
    }

    private async Task<IActionResult> MutateAsync(string namespaceSlug, string project, long number, Func<long, string, bool, Task<IssueMutationResult>> mutation, CancellationToken cancellationToken)
    {
        var access = await ResolveReadableAsync(namespaceSlug, project, cancellationToken);
        if (access is null || string.IsNullOrWhiteSpace(_currentUser.UserId)) return NotFound();
        var result = await mutation(access.Value.Address.RepositoryId, _currentUser.UserId, access.Value.IsOwner);
        if (result != IssueMutationResult.Succeeded) TempData["IssueError"] = MutationMessage(result);
        return RedirectToAction(nameof(Detail), new { namespaceSlug, project, number });
    }

    private async Task<(RepositoryAddressResolution Address, bool IsOwner)?> ResolveReadableAsync(string namespaceSlug, string project, CancellationToken cancellationToken)
    {
        var address = await _addressResolver.ResolveAsync(namespaceSlug, project, cancellationToken);
        if (address is null || address.UsedAlias || !await CanReadAsync(address.RepositoryId)) return null;
        var owner = (await _authorizationService.AuthorizeAsync(User, new RepositoryAuthorizationResource(address.RepositoryId), AuthorizationPolicies.RepositoryOwner)).Succeeded;
        return (address, owner);
    }

    private async Task<bool> CanReadAsync(long repositoryId) =>
        (await _authorizationService.AuthorizeAsync(User, new RepositoryAuthorizationResource(repositoryId), AuthorizationPolicies.RepositoryRead)).Succeeded;

    private void AddMutationError(IssueMutationResult result) => ModelState.AddModelError(string.Empty, MutationMessage(result));
    private static string MutationMessage(IssueMutationResult result) => result switch
    {
        IssueMutationResult.Conflict => "This issue changed while you were editing it. Reload and try again.",
        IssueMutationResult.Locked => "This discussion is locked.",
        IssueMutationResult.RateLimited => "Too many discussion actions. Try again shortly.",
        IssueMutationResult.Forbidden => "You do not have permission to perform this action.",
        IssueMutationResult.Invalid => "The supplied issue data is invalid.",
        _ => "The issue could not be updated."
    };
}
