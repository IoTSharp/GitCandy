using System.Security.Claims;
using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Git;
using GitCandy.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GitCandy.Controllers;

/// <summary>
/// Git LFS v2 basic transfer API。
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("git/{project}.git/info/lfs")]
[Route("git/{project}/info/lfs")]
public sealed class GitLfsController(
    IRepositoryService repositoryService,
    IGitLfsObjectStore objectStore,
    IAuthenticationService authenticationService,
    IAuthorizationService authorizationService,
    IOptions<GitLfsOptions> options) : ControllerBase
{
    private const string LfsJsonMediaType = "application/vnd.git-lfs+json";
    private static readonly ClaimsPrincipal AnonymousPrincipal = new(new ClaimsIdentity());
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>();
    private readonly IRepositoryService _repositoryService = repositoryService;
    private readonly IGitLfsObjectStore _objectStore = objectStore;
    private readonly IAuthenticationService _authenticationService = authenticationService;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly GitLfsOptions _options = options.Value;

    /// <summary>协商 basic upload/download actions。</summary>
    [HttpPost("objects/batch")]
    [Consumes(LfsJsonMediaType, "application/json")]
    [Produces(LfsJsonMediaType)]
    public async Task<IActionResult> Batch(
        string project,
        GitLfsBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return NotFound();
        }

        var isUpload = string.Equals(request.Operation, "upload", StringComparison.Ordinal);
        if (!isUpload && !string.Equals(request.Operation, "download", StringComparison.Ordinal))
        {
            return LfsError(StatusCodes.Status422UnprocessableEntity, "Unsupported LFS operation.");
        }

        if (request.Transfers.Count > 0
            && !request.Transfers.Contains("basic", StringComparer.Ordinal))
        {
            return LfsError(StatusCodes.Status422UnprocessableEntity, "Only the basic transfer is supported.");
        }

        if (request.HashAlgorithm is not null
            && !string.Equals(request.HashAlgorithm, "sha256", StringComparison.Ordinal))
        {
            return LfsError(StatusCodes.Status422UnprocessableEntity, "Only SHA-256 LFS object IDs are supported.");
        }

        var access = await AuthorizeRepositoryAsync(project, isUpload, cancellationToken);
        if (access.Failure is not null)
        {
            return access.Failure;
        }

        var authenticated = access.Principal?.Identity?.IsAuthenticated == true;
        var objects = request.Objects.Select(item => CreateObjectResponse(
            project,
            item,
            isUpload,
            authenticated)).ToArray();
        return LfsJson(new GitLfsBatchResponse("basic", objects));
    }

    /// <summary>流式上传并原子提交 LFS 对象。</summary>
    [HttpPut("objects/{oid}")]
    public async Task<IActionResult> Upload(
        string project,
        string oid,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeRepositoryAsync(project, requireWrite: true, cancellationToken);
        if (access.Failure is not null)
        {
            return access.Failure;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.OperationTimeout);
        try
        {
            await _objectStore.WriteAsync(
                project,
                oid,
                Request.ContentLength,
                Request.Body,
                timeout.Token);
            return Ok();
        }
        catch (ArgumentException)
        {
            return LfsError(StatusCodes.Status422UnprocessableEntity, "The LFS OID is invalid.");
        }
        catch (InvalidDataException exception)
        {
            return LfsError(StatusCodes.Status422UnprocessableEntity, exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LfsError(StatusCodes.Status408RequestTimeout, "The LFS upload timed out.");
        }
    }

    /// <summary>流式下载 LFS 对象，支持 HTTP range。</summary>
    [HttpGet("objects/{oid}")]
    public async Task<IActionResult> Download(
        string project,
        string oid,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeRepositoryAsync(project, requireWrite: false, cancellationToken);
        if (access.Failure is not null)
        {
            return access.Failure;
        }

        try
        {
            var info = _objectStore.GetInfo(project, oid);
            return info is null
                ? LfsError(StatusCodes.Status404NotFound, "The LFS object does not exist.")
                : File(
                    _objectStore.OpenRead(project, oid),
                    "application/octet-stream",
                    enableRangeProcessing: true);
        }
        catch (ArgumentException)
        {
            return LfsError(StatusCodes.Status404NotFound, "The LFS object does not exist.");
        }
    }

    /// <summary>检查 LFS 对象是否存在。</summary>
    [HttpHead("objects/{oid}")]
    public async Task<IActionResult> Exists(
        string project,
        string oid,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeRepositoryAsync(project, requireWrite: false, cancellationToken);
        if (access.Failure is not null)
        {
            return access.Failure;
        }

        try
        {
            var info = _objectStore.GetInfo(project, oid);
            if (info is null)
            {
                return NotFound();
            }

            Response.ContentLength = info.Size;
            return Ok();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    /// <summary>验证已上传对象的 OID 和 size。</summary>
    [HttpPost("objects/{oid}/verify")]
    [Consumes(LfsJsonMediaType, "application/json")]
    public async Task<IActionResult> Verify(
        string project,
        string oid,
        GitLfsVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeRepositoryAsync(project, requireWrite: true, cancellationToken);
        if (access.Failure is not null)
        {
            return access.Failure;
        }

        if (!string.Equals(oid, request.Oid, StringComparison.OrdinalIgnoreCase))
        {
            return LfsError(StatusCodes.Status422UnprocessableEntity, "The verify OID does not match the URL.");
        }

        try
        {
            var info = _objectStore.GetInfo(project, oid);
            return info is not null && info.Size == request.Size
                ? Ok()
                : LfsError(StatusCodes.Status422UnprocessableEntity, "The stored LFS object does not match the requested size.");
        }
        catch (ArgumentException)
        {
            return LfsError(StatusCodes.Status422UnprocessableEntity, "The LFS OID is invalid.");
        }
    }

    private GitLfsObjectResponse CreateObjectResponse(
        string project,
        GitLfsObjectRequest request,
        bool isUpload,
        bool authenticated)
    {
        if (request.Size > _options.MaxObjectBytes)
        {
            return ObjectError(request, StatusCodes.Status422UnprocessableEntity, "The LFS object exceeds the configured size limit.", authenticated);
        }

        GitLfsObjectInfo? existing;
        try
        {
            existing = _objectStore.GetInfo(project, request.Oid);
        }
        catch (ArgumentException)
        {
            return ObjectError(request, StatusCodes.Status422UnprocessableEntity, "The LFS OID is invalid.", authenticated);
        }

        if (existing is not null && existing.Size != request.Size)
        {
            return ObjectError(request, StatusCodes.Status422UnprocessableEntity, "The stored object has a different size.", authenticated);
        }

        var objectUrl = BuildObjectUrl(project, request.Oid);
        if (!isUpload)
        {
            return existing is null
                ? ObjectError(request, StatusCodes.Status404NotFound, "The LFS object does not exist.", authenticated)
                : new GitLfsObjectResponse(
                    request.Oid,
                    request.Size,
                    authenticated,
                    new Dictionary<string, GitLfsAction>
                    {
                        ["download"] = new(objectUrl, EmptyHeaders)
                    });
        }

        if (existing is not null)
        {
            return new GitLfsObjectResponse(request.Oid, request.Size, authenticated);
        }

        if (!_objectStore.CanStore(project, request.Size))
        {
            return ObjectError(request, StatusCodes.Status507InsufficientStorage, "The repository LFS quota would be exceeded.", authenticated);
        }

        return new GitLfsObjectResponse(
            request.Oid,
            request.Size,
            authenticated,
            new Dictionary<string, GitLfsAction>
            {
                ["upload"] = new(objectUrl, EmptyHeaders),
                ["verify"] = new($"{objectUrl}/verify", EmptyHeaders)
            });
    }

    private async Task<RepositoryAccess> AuthorizeRepositoryAsync(
        string project,
        bool requireWrite,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new RepositoryAccess(null, NotFound());
        }

        var authentication = await _authenticationService.AuthenticateAsync(
            HttpContext,
            GitCandyAuthenticationSchemes.GitBasic);
        if (authentication.Failure is not null)
        {
            return new RepositoryAccess(null, await ChallengeGitClientAsync());
        }

        var principal = authentication.Principal ?? AnonymousPrincipal;
        RepositorySummary? repository;
        try
        {
            repository = await _repositoryService.FindRepositoryAsync(project, cancellationToken);
        }
        catch (ArgumentException)
        {
            return new RepositoryAccess(principal, NotFound());
        }

        if (repository is null)
        {
            return new RepositoryAccess(principal, NotFound());
        }

        var authorized = await _authorizationService.AuthorizeAsync(
            principal,
            new RepositoryAuthorizationResource(repository.Name),
            requireWrite ? AuthorizationPolicies.RepositoryWrite : AuthorizationPolicies.RepositoryRead);
        if (!authorized.Succeeded)
        {
            return new RepositoryAccess(
                principal,
                principal.Identity?.IsAuthenticated == true
                    ? StatusCode(StatusCodes.Status403Forbidden)
                    : await ChallengeGitClientAsync());
        }

        return new RepositoryAccess(principal, null);
    }

    private async Task<IActionResult> ChallengeGitClientAsync()
    {
        await _authenticationService.ChallengeAsync(
            HttpContext,
            GitCandyAuthenticationSchemes.GitBasic,
            properties: null);
        return new EmptyResult();
    }

    private string BuildObjectUrl(string project, string oid)
    {
        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}/git/{Uri.EscapeDataString(project)}.git/info/lfs/objects/{oid}";
    }

    private ObjectResult LfsJson(object value, int statusCode = StatusCodes.Status200OK)
    {
        return new ObjectResult(value)
        {
            StatusCode = statusCode,
            ContentTypes = { LfsJsonMediaType }
        };
    }

    private ObjectResult LfsError(int statusCode, string message)
    {
        return LfsJson(new GitLfsError(statusCode, message), statusCode);
    }

    private static GitLfsObjectResponse ObjectError(
        GitLfsObjectRequest request,
        int statusCode,
        string message,
        bool authenticated)
    {
        return new GitLfsObjectResponse(
            request.Oid,
            request.Size,
            authenticated,
            Error: new GitLfsError(statusCode, message));
    }

    private sealed record RepositoryAccess(ClaimsPrincipal? Principal, IActionResult? Failure);
}
