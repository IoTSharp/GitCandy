using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Git;
using GitCandy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

/// <summary>规范 namespace/repository Web 地址和历史 Web 地址兼容入口。</summary>
[AllowAnonymous]
[AutoValidateAntiforgeryToken]
public sealed class NamespaceRepositoryController(
    IRepositoryAddressResolver addressResolver,
    IRepositoryBrowserService repositoryBrowserService,
    IManagedGitRepositoryService managedGitRepositoryService,
    IGitServiceFactory gitServiceFactory,
    IAuthorizationService authorizationService,
    ICurrentUser currentUser) : CandyControllerBase
{
    private const string AddressChangedMessage = "This repository address changed. Update your bookmark and Git remote to the canonical URL.";
    private readonly IRepositoryAddressResolver _addressResolver = addressResolver;
    private readonly IRepositoryBrowserService _repositoryBrowserService = repositoryBrowserService;
    private readonly IManagedGitRepositoryService _managedGitRepositoryService = managedGitRepositoryService;
    private readonly IGitServiceFactory _gitServiceFactory = gitServiceFactory;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>显示规范仓库主页，alias 命中时使用 308 跳转。</summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var address = await _addressResolver.ResolveAsync(namespaceSlug, project, cancellationToken);
        return address is null
            ? NotFound()
            : await RenderOrRedirectAsync(address, cancellationToken);
    }

    /// <summary>将带 .git 的 Web 页面地址规范化为无后缀地址。</summary>
    [HttpGet]
    public async Task<IActionResult> GitCompatibility(
        string namespaceSlug,
        string project,
        CancellationToken cancellationToken)
    {
        var address = await _addressResolver.ResolveAsync(namespaceSlug, project, cancellationToken);
        if (address is null)
        {
            return NotFound();
        }

        var denied = await RequireReadAsync(address.RepositoryId);
        return denied ?? RedirectCanonical(address.CanonicalPath);
    }

    /// <summary>将旧 /git/{project}[.git] Web 地址迁移到规范地址。</summary>
    [HttpGet]
    public async Task<IActionResult> Legacy(string project, CancellationToken cancellationToken)
    {
        var address = await _addressResolver.ResolveLegacyAsync(project, cancellationToken);
        if (address is null)
        {
            return NotFound();
        }

        var denied = await RequireReadAsync(address.RepositoryId);
        return denied ?? RedirectCanonical(address.CanonicalPath);
    }

    [HttpGet("{namespaceSlug}/{project}/branches")]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Branches(string namespaceSlug, string project, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryRead, cancellationToken);
        if (access.Result is not null) return access.Result;
        var address = access.Address!;
        if (address.UsedAlias) return RedirectCanonical($"{address.CanonicalPath}/branches");
        try
        {
            var branches = _repositoryBrowserService.ReadBranches(_gitServiceFactory.Create(address.StorageName), cancellationToken);
            return View("~/Views/Repository/Branches.cshtml", new RepositoryBranchesViewModel(
                address.NamespaceSlug, address.RepositorySlug, branches, await CanWriteAsync(address.RepositoryId)));
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception)) { return NotFound(); }
    }

    [HttpPost("{namespaceSlug}/{project}/branches/delete")]
    public async Task<IActionResult> DeleteBranch(string namespaceSlug, string project, string name, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryWrite, cancellationToken);
        if (access.Result is not null) return access.Result;
        try
        {
            _managedGitRepositoryService.DeleteBranch(_gitServiceFactory.Create(access.Address!.StorageName), name, cancellationToken);
            return Redirect($"{access.Address.CanonicalPath}/branches");
        }
        catch (ArgumentException) { return BadRequest(); }
        catch (InvalidOperationException) { return Conflict("The default branch cannot be deleted."); }
    }

    [HttpGet("{namespaceSlug}/{project}/tags")]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Tags(string namespaceSlug, string project, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryRead, cancellationToken);
        if (access.Result is not null) return access.Result;
        var address = access.Address!;
        if (address.UsedAlias) return RedirectCanonical($"{address.CanonicalPath}/tags");
        try
        {
            var tags = _repositoryBrowserService.ReadTags(_gitServiceFactory.Create(address.StorageName), cancellationToken);
            return View("~/Views/Repository/Tags.cshtml", new RepositoryTagsViewModel(
                address.NamespaceSlug, address.RepositorySlug, tags, await CanWriteAsync(address.RepositoryId)));
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception)) { return NotFound(); }
    }

    [HttpPost("{namespaceSlug}/{project}/tags/delete")]
    public async Task<IActionResult> DeleteTag(string namespaceSlug, string project, string name, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryWrite, cancellationToken);
        if (access.Result is not null) return access.Result;
        try
        {
            _managedGitRepositoryService.DeleteTag(_gitServiceFactory.Create(access.Address!.StorageName), name, cancellationToken);
            return Redirect($"{access.Address.CanonicalPath}/tags");
        }
        catch (ArgumentException) { return BadRequest(); }
    }

    [HttpGet("{namespaceSlug}/{project}/contributors")]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Contributors(string namespaceSlug, string project, string? revision, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryRead, cancellationToken);
        if (access.Result is not null) return access.Result;
        var address = access.Address!;
        if (address.UsedAlias) return RedirectCanonical($"{address.CanonicalPath}/contributors");
        try
        {
            var statistics = _repositoryBrowserService.ReadStatistics(_gitServiceFactory.Create(address.StorageName), revision, cancellationToken);
            if (statistics is null && !string.IsNullOrWhiteSpace(revision))
            {
                return NotFound();
            }
            return View("~/Views/Repository/Contributors.cshtml", new RepositoryContributorsViewModel(address.NamespaceSlug, address.RepositorySlug, statistics));
        }
        catch (Exception exception) when (IsInvalidRepositoryRequest(exception)) { return NotFound(); }
    }

    [HttpGet("{namespaceSlug}/{project}/archive")]
    [RequestTimeout(RepositoryBrowserOptions.RequestTimeoutPolicyName)]
    public async Task<IActionResult> Archive(string namespaceSlug, string project, string? revision, CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(namespaceSlug, project, AuthorizationPolicies.RepositoryRead, cancellationToken);
        if (access.Result is not null) return access.Result;
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(project)}.zip";
        var result = await _repositoryBrowserService.WriteArchiveAsync(
            _gitServiceFactory.Create(access.Address!.StorageName), revision, Response.Body, cancellationToken);
        return result is null ? NotFound() : new EmptyResult();
    }

    private async Task<IActionResult> RenderOrRedirectAsync(
        RepositoryAddressResolution address,
        CancellationToken cancellationToken)
    {
        var denied = await RequireReadAsync(address.RepositoryId);
        if (denied is not null)
        {
            return denied;
        }

        if (address.UsedAlias)
        {
            return RedirectCanonical(address.CanonicalPath);
        }

        var context = _gitServiceFactory.Create(address.StorageName);
        try
        {
            var tree = _repositoryBrowserService.ReadTree(context, revision: null, path: null, cancellationToken);
            if (tree is null && _managedGitRepositoryService.ReadSnapshot(context).HeadCommitId is not null)
            {
                return NotFound();
            }

            ViewData["CanonicalUrl"] = address.CanonicalPath;
            return View(
                "~/Views/Repository/Tree.cshtml",
                new RepositoryTreeViewModel { RepositoryName = address.RepositorySlug, Tree = tree });
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or GitRepositoryNotFoundException
            or LibGit2Sharp.LibGit2SharpException)
        {
            return NotFound();
        }
    }

    private async Task<IActionResult?> RequireReadAsync(long repositoryId)
    {
        var result = await _authorizationService.AuthorizeAsync(
            User,
            new RepositoryAuthorizationResource(repositoryId),
            AuthorizationPolicies.RepositoryRead);
        if (result.Succeeded)
        {
            return null;
        }

        return _currentUser.IsAuthenticated ? Forbid() : Challenge();
    }

    private async Task<(RepositoryAddressResolution? Address, IActionResult? Result)> ResolveAccessAsync(
        string namespaceSlug, string project, string policy, CancellationToken cancellationToken)
    {
        RepositoryAddressResolution? address;
        try { address = await _addressResolver.ResolveAsync(namespaceSlug, project, cancellationToken); }
        catch (ArgumentException) { return (null, NotFound()); }
        if (address is null) return (null, NotFound());
        var result = await _authorizationService.AuthorizeAsync(User, new RepositoryAuthorizationResource(address.RepositoryId), policy);
        return result.Succeeded ? (address, null) : (null, _currentUser.IsAuthenticated ? Forbid() : Challenge());
    }

    private async Task<bool> CanWriteAsync(long repositoryId) =>
        (await _authorizationService.AuthorizeAsync(User, new RepositoryAuthorizationResource(repositoryId), AuthorizationPolicies.RepositoryWrite)).Succeeded;

    private static bool IsInvalidRepositoryRequest(Exception exception) => exception is ArgumentException or InvalidOperationException or GitRepositoryNotFoundException or LibGit2Sharp.LibGit2SharpException;

    private IActionResult RedirectCanonical(string path)
    {
        TempData["CanonicalAddressChanged"] = AddressChangedMessage;
        return new RedirectResult(path + Request.QueryString, permanent: true, preserveMethod: true);
    }
}
