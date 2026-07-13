using System.Globalization;
using System.Security.Claims;
using GitCandy.Application;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Credentials;
using GitCandy.Git;
using GitCandy.Integrations;
using GitCandy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GitCandy.Controllers;

[ApiController]
[Route("api/v1/repositories/{namespaceSlug}/{project}/commits/{sha}")]
[Produces("application/json")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CommitChecksApiController(
    IRepositoryAddressResolver addressResolver,
    IAuthorizationService authorizationService,
    IRepositoryBrowserService repositoryBrowserService,
    IGitServiceFactory gitServiceFactory,
    ICommitCheckService commitCheckService) : ControllerBase
{
    private readonly IRepositoryAddressResolver _addressResolver = addressResolver;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly IRepositoryBrowserService _repositoryBrowserService = repositoryBrowserService;
    private readonly IGitServiceFactory _gitServiceFactory = gitServiceFactory;
    private readonly ICommitCheckService _commitCheckService = commitCheckService;

    [HttpPost("statuses")]
    [Authorize(Policy = AuthorizationPolicies.ApiWrite)]
    [EnableRateLimiting(ApiRateLimitPolicies.Write)]
    public async Task<IActionResult> SetStatus(
        string namespaceSlug,
        string project,
        string sha,
        CommitStatusRequest request,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(
            namespaceSlug,
            project,
            sha,
            AuthorizationPolicies.RepositoryWrite,
            cancellationToken);
        if (access.Result is not null) return access.Result;
        if (!TryParseStatusState(request.State, out var state))
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Invalid commit status state.");
        }
        return await UpsertAsync(
            access.Address!,
            new CommitCheckUpdate(
                sha,
                CommitCheckKind.Status,
                request.Context,
                state,
                request.Description,
                request.TargetUrl,
                request.ExternalId),
            cancellationToken);
    }

    [HttpPost("checks")]
    [Authorize(Policy = AuthorizationPolicies.ApiWrite)]
    [EnableRateLimiting(ApiRateLimitPolicies.Write)]
    public async Task<IActionResult> SetCheck(
        string namespaceSlug,
        string project,
        string sha,
        CheckRunRequest request,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(
            namespaceSlug,
            project,
            sha,
            AuthorizationPolicies.RepositoryWrite,
            cancellationToken);
        if (access.Result is not null) return access.Result;
        if (!TryParseCheckState(request.State, out var state))
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Invalid check state.");
        }
        return await UpsertAsync(
            access.Address!,
            new CommitCheckUpdate(
                sha,
                CommitCheckKind.Check,
                request.Name,
                state,
                request.Summary,
                request.DetailsUrl,
                request.ExternalId),
            cancellationToken);
    }

    [HttpGet("checks")]
    [Authorize(Policy = AuthorizationPolicies.ApiRead)]
    public async Task<IActionResult> GetChecks(
        string namespaceSlug,
        string project,
        string sha,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAsync(
            namespaceSlug,
            project,
            sha,
            AuthorizationPolicies.RepositoryRead,
            cancellationToken);
        if (access.Result is not null) return access.Result;
        var checks = await _commitCheckService.GetForCommitAsync(
            access.Address!.RepositoryId,
            sha,
            cancellationToken);
        return Ok(checks.Select(ToResponse));
    }

    private async Task<IActionResult> UpsertAsync(
        RepositoryAddressResolution address,
        CommitCheckUpdate update,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId)) return Forbid();
        long? credentialId = null;
        var credentialValue = User.FindFirstValue(CredentialClaimTypes.CredentialId);
        if (long.TryParse(credentialValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCredentialId))
        {
            credentialId = parsedCredentialId;
        }
        var check = await _commitCheckService.UpsertAsync(
            address.RepositoryId,
            actorUserId,
            credentialId,
            update,
            cancellationToken);
        return check is null
            ? Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The check context, state, or target URL is invalid.")
            : Ok(ToResponse(check));
    }

    private async Task<(RepositoryAddressResolution? Address, IActionResult? Result)> ResolveAsync(
        string namespaceSlug,
        string project,
        string sha,
        string policy,
        CancellationToken cancellationToken)
    {
        RepositoryAddressResolution? address;
        try
        {
            address = await _addressResolver.ResolveAsync(namespaceSlug, project, cancellationToken);
        }
        catch (ArgumentException)
        {
            return (null, NotFound());
        }
        if (address is null || address.UsedAlias) return (null, NotFound());
        var authorized = await _authorizationService.AuthorizeAsync(
            User,
            new RepositoryAuthorizationResource(address.RepositoryId),
            policy);
        if (!authorized.Succeeded) return (null, Forbid());
        try
        {
            var commit = _repositoryBrowserService.ReadCommit(
                _gitServiceFactory.Create(address.StorageName),
                sha,
                cancellationToken);
            return commit is null ? (null, NotFound()) : (address, null);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or GitRepositoryNotFoundException
            or LibGit2Sharp.LibGit2SharpException)
        {
            return (null, NotFound());
        }
    }

    private static object ToResponse(CommitCheckSummary check) => new
    {
        id = check.Id,
        sha = check.Sha,
        kind = check.Kind == CommitCheckKind.Status ? "status" : "check",
        context = check.Context,
        state = ToApiState(check.State),
        description = check.Description,
        targetUrl = check.TargetUrl,
        externalId = check.ExternalId,
        createdAt = check.CreatedAt,
        updatedAt = check.UpdatedAt
    };

    private static bool TryParseStatusState(string value, out CommitCheckState state)
    {
        state = value.Trim().ToLowerInvariant() switch
        {
            "pending" => CommitCheckState.Pending,
            "success" => CommitCheckState.Success,
            "failure" => CommitCheckState.Failure,
            "error" => CommitCheckState.Error,
            _ => (CommitCheckState)(-1)
        };
        return Enum.IsDefined(state);
    }

    private static bool TryParseCheckState(string value, out CommitCheckState state)
    {
        state = value.Trim().ToLowerInvariant() switch
        {
            "queued" or "pending" => CommitCheckState.Pending,
            "in_progress" or "running" => CommitCheckState.Running,
            "success" => CommitCheckState.Success,
            "failure" => CommitCheckState.Failure,
            "error" or "timed_out" or "action_required" => CommitCheckState.Error,
            "cancelled" => CommitCheckState.Cancelled,
            "neutral" => CommitCheckState.Neutral,
            "skipped" => CommitCheckState.Skipped,
            _ => (CommitCheckState)(-1)
        };
        return Enum.IsDefined(state);
    }

    private static string ToApiState(CommitCheckState state) => state switch
    {
        CommitCheckState.Pending => "pending",
        CommitCheckState.Running => "in_progress",
        CommitCheckState.Success => "success",
        CommitCheckState.Failure => "failure",
        CommitCheckState.Error => "error",
        CommitCheckState.Cancelled => "cancelled",
        CommitCheckState.Neutral => "neutral",
        CommitCheckState.Skipped => "skipped",
        _ => "error"
    };
}
