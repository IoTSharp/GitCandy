using System.Security.Claims;
using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Git;
using GitCandy.Models;
using GitCandy.Workspace;
using GitCandy.Credentials;
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
[Route("{namespaceSlug}/{project}.git/info/lfs")]
public sealed class GitLfsController(
    IRepositoryAddressResolver addressResolver,
    IGitLfsObjectStore objectStore,
    IAuthenticationService authenticationService,
    IAuthorizationService authorizationService,
    IRepositoryMetricRecorder metricRecorder,
    IOptions<GitLfsOptions> options,
    ILogger<GitLfsController> logger) : ControllerBase
{
    private const string LfsJsonMediaType = "application/vnd.git-lfs+json";
    private static readonly ClaimsPrincipal AnonymousPrincipal = new(new ClaimsIdentity());
    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>();
    private readonly IRepositoryAddressResolver _addressResolver = addressResolver;
    private readonly IGitLfsObjectStore _objectStore = objectStore;
    private readonly IAuthenticationService _authenticationService = authenticationService;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly IRepositoryMetricRecorder _metricRecorder = metricRecorder;
    private readonly GitLfsOptions _options = options.Value;
    private readonly ILogger<GitLfsController> _logger = logger;

    /// <summary>协商 basic upload/download actions。</summary>
    [HttpPost("objects/batch")]
    [Consumes(LfsJsonMediaType, "application/json")]
    [Produces(LfsJsonMediaType)]
    public async Task<IActionResult> Batch(
        string namespaceSlug,
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

        var access = await AuthorizeRepositoryAsync(namespaceSlug, project, isUpload, cancellationToken);
        if (access.Failure is not null)
        {
            return access.Failure;
        }

        var authenticated = access.Principal?.Identity?.IsAuthenticated == true;
        var objects = request.Objects.Select(item => CreateObjectResponse(
            access.Address!,
            namespaceSlug,
            project,
            item,
            isUpload,
            authenticated)).ToArray();
        return LfsJson(new GitLfsBatchResponse("basic", objects));
    }

    /// <summary>流式上传并原子提交 LFS 对象。</summary>
    [HttpPut("objects/{oid}")]
    public async Task<IActionResult> Upload(
        string namespaceSlug,
        string project,
        string oid,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeRepositoryAsync(namespaceSlug, project, requireWrite: true, cancellationToken);
        if (access.Failure is not null)
        {
            return access.Failure;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.OperationTimeout);
        try
        {
            await _objectStore.WriteAsync(
                access.Address!.StorageName,
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
        string namespaceSlug,
        string project,
        string oid,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeRepositoryAsync(namespaceSlug, project, requireWrite: false, cancellationToken);
        if (access.Failure is not null)
        {
            return access.Failure;
        }

        try
        {
            var info = _objectStore.GetInfo(access.Address!.StorageName, oid);
            if (info is not null)
            {
                var repositoryId = access.Address.RepositoryId;
                Response.OnCompleted(() => RecordSuccessfulDownloadAsync(repositoryId));
            }
            return info is null
                ? LfsError(StatusCodes.Status404NotFound, "The LFS object does not exist.")
                : File(
                    _objectStore.OpenRead(access.Address.StorageName, oid),
                    "application/octet-stream",
                    enableRangeProcessing: true);
        }
        catch (ArgumentException)
        {
            return LfsError(StatusCodes.Status404NotFound, "The LFS object does not exist.");
        }
    }

    private async Task RecordSuccessfulDownloadAsync(long repositoryId)
    {
        try
        {
            await _metricRecorder.RecordSuccessfulDownloadAsync(repositoryId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "LFS download metrics could not be recorded for repository {RepositoryId}.", repositoryId);
        }
    }

    /// <summary>检查 LFS 对象是否存在。</summary>
    [HttpHead("objects/{oid}")]
    public async Task<IActionResult> Exists(
        string namespaceSlug,
        string project,
        string oid,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeRepositoryAsync(namespaceSlug, project, requireWrite: false, cancellationToken);
        if (access.Failure is not null)
        {
            return access.Failure;
        }

        try
        {
            var info = _objectStore.GetInfo(access.Address!.StorageName, oid);
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
        string namespaceSlug,
        string project,
        string oid,
        GitLfsVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var access = await AuthorizeRepositoryAsync(namespaceSlug, project, requireWrite: true, cancellationToken);
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
            var info = _objectStore.GetInfo(access.Address!.StorageName, oid);
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
        RepositoryAddressResolution address,
        string namespaceSlug,
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
            existing = _objectStore.GetInfo(address.StorageName, request.Oid);
        }
        catch (ArgumentException)
        {
            return ObjectError(request, StatusCodes.Status422UnprocessableEntity, "The LFS OID is invalid.", authenticated);
        }

        if (existing is not null && existing.Size != request.Size)
        {
            return ObjectError(request, StatusCodes.Status422UnprocessableEntity, "The stored object has a different size.", authenticated);
        }

        var objectUrl = BuildObjectUrl(namespaceSlug, project, request.Oid);
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

        if (!_objectStore.CanStore(address.StorageName, request.Size))
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
        string namespaceSlug,
        string project,
        bool requireWrite,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new RepositoryAccess(null, null, NotFound());
        }

        var authentication = await _authenticationService.AuthenticateAsync(
            HttpContext,
            GitCandyAuthenticationSchemes.GitBasic);
        if (authentication.Failure is not null)
        {
            return new RepositoryAccess(null, null, await ChallengeGitClientAsync());
        }

        var principal = authentication.Principal ?? AnonymousPrincipal;
        var requiredScope = requireWrite
            ? PersonalAccessTokenScopes.GitWrite
            : PersonalAccessTokenScopes.GitRead;
        if (!principal.HasPersonalAccessTokenScope(requiredScope))
        {
            return new RepositoryAccess(
                principal,
                null,
                StatusCode(StatusCodes.Status403Forbidden));
        }
        RepositoryAddressResolution? address;
        try
        {
            address = await _addressResolver.ResolveAsync(namespaceSlug, project, cancellationToken);
        }
        catch (ArgumentException)
        {
            return new RepositoryAccess(principal, null, NotFound());
        }

        if (address is null || address.UsedAlias)
        {
            return new RepositoryAccess(principal, null, NotFound());
        }

        var authorized = await _authorizationService.AuthorizeAsync(
            principal,
            new RepositoryAuthorizationResource(address.RepositoryId),
            requireWrite ? AuthorizationPolicies.RepositoryWrite : AuthorizationPolicies.RepositoryRead);
        if (!authorized.Succeeded)
        {
            return new RepositoryAccess(
                principal,
                address,
                principal.Identity?.IsAuthenticated == true
                    ? StatusCode(StatusCodes.Status403Forbidden)
                    : await ChallengeGitClientAsync());
        }

        return new RepositoryAccess(principal, address, null);
    }

    private async Task<IActionResult> ChallengeGitClientAsync()
    {
        await _authenticationService.ChallengeAsync(
            HttpContext,
            GitCandyAuthenticationSchemes.GitBasic,
            properties: null);
        return new EmptyResult();
    }

    private string BuildObjectUrl(string namespaceSlug, string project, string oid)
    {
        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}/{Uri.EscapeDataString(namespaceSlug)}/{Uri.EscapeDataString(project)}.git/info/lfs/objects/{oid}";
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

    private sealed record RepositoryAccess(
        ClaimsPrincipal? Principal,
        RepositoryAddressResolution? Address,
        IActionResult? Failure);
}
