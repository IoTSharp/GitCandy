using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using GitCandy.Application;
using GitCandy.Authentication;
using GitCandy.Authorization;
using GitCandy.Configuration;
using GitCandy.Git;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace GitCandy.Controllers;

/// <summary>
/// ASP.NET Core Git Smart HTTP endpoint。
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class GitController(
    IRepositoryService repositoryService,
    IGitServiceFactory gitServiceFactory,
    IGitTransportBackend transportBackend,
    IAuthenticationService authenticationService,
    IAuthorizationService authorizationService,
    IOptions<GitSmartHttpOptions> options,
    ILogger<GitController> logger)
    : ControllerBase
{
    private const string GitProtocolHeaderName = "Git-Protocol";
    private static readonly ClaimsPrincipal AnonymousPrincipal = new(new ClaimsIdentity());
    private readonly IRepositoryService _repositoryService = repositoryService;
    private readonly IGitServiceFactory _gitServiceFactory = gitServiceFactory;
    private readonly IGitTransportBackend _transportBackend = transportBackend;
    private readonly IAuthenticationService _authenticationService = authenticationService;
    private readonly IAuthorizationService _authorizationService = authorizationService;
    private readonly GitSmartHttpOptions _options = options.Value;
    private readonly ILogger<GitController> _logger = logger;

    /// <summary>
    /// 处理 Git Smart HTTP discovery、upload-pack 和 receive-pack 请求。
    /// </summary>
    /// <param name="project">公开 URL 中的仓库名。</param>
    /// <param name="verb">Git Smart HTTP verb。</param>
    /// <param name="service">discovery query 中的 Git service。</param>
    /// <returns>流式 Git Smart HTTP 响应。</returns>
    public async Task<IActionResult> Smart(
        string project,
        string? verb = null,
        [FromQuery] string? service = null)
    {
        if (IsBrowserFallback(verb, service))
        {
            return RedirectToAction("Tree", "Repository", new { name = project });
        }

        SetNoCacheHeaders();

        var operationResult = ResolveOperation(verb, service);
        if (operationResult.Operation is not { } operation)
        {
            return StatusCode(
                operationResult.ErrorStatusCode
                ?? StatusCodes.Status500InternalServerError);
        }

        if (!string.Equals(
            Request.Method,
            operation.HttpMethod,
            StringComparison.OrdinalIgnoreCase))
        {
            Response.Headers[HeaderNames.Allow] = operation.HttpMethod;
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        if (!TryConfigureRequestBodyLimit())
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var authenticationResult = await _authenticationService.AuthenticateAsync(
            HttpContext,
            GitCandyAuthenticationSchemes.GitBasic);
        if (authenticationResult.Failure is not null)
        {
            return await ChallengeGitClientAsync();
        }

        var principal = authenticationResult.Principal ?? AnonymousPrincipal;
        RepositorySummary? repository;
        try
        {
            repository = await _repositoryService.FindRepositoryAsync(
                project,
                HttpContext.RequestAborted);
        }
        catch (ArgumentException)
        {
            return NotFound();
        }

        if (repository is null)
        {
            return NotFound();
        }

        var policy = operation.Service == GitTransportService.ReceivePack
            ? AuthorizationPolicies.RepositoryWrite
            : AuthorizationPolicies.RepositoryRead;
        var authorizationResult = await _authorizationService.AuthorizeAsync(
            principal,
            new RepositoryAuthorizationResource(repository.Name),
            policy);
        if (!authorizationResult.Succeeded)
        {
            return principal.Identity?.IsAuthenticated == true
                ? StatusCode(StatusCodes.Status403Forbidden)
                : await ChallengeGitClientAsync();
        }

        GitRepositoryContext repositoryContext;
        try
        {
            repositoryContext = _gitServiceFactory.Create(repository.Name);
            _transportBackend.EnsureRepositoryExists(repositoryContext);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or GitRepositoryNotFoundException)
        {
            _logger.LogWarning(
                "Git repository {RepositoryName} has no safe physical repository path.",
                repository.Name);
            return NotFound();
        }

        var protocolVersion = GetProtocolVersion();
        Response.ContentType = operation.AdvertiseRefs
            ? $"application/x-{operation.ServiceName}-advertisement"
            : $"application/x-{operation.ServiceName}-result";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            HttpContext.RequestAborted);
        timeoutSource.CancelAfter(_options.RequestTimeout);

        GZipStream? gzipStream = null;
        try
        {
            Stream input;
            if (operation.AdvertiseRefs)
            {
                input = Stream.Null;
                if (!UsesProtocolV2Advertisement(operation, protocolVersion))
                {
                    await WriteServiceAnnouncementAsync(
                        operation.ServiceName,
                        timeoutSource.Token);
                }
            }
            else
            {
                var contentEncoding = Request.Headers.ContentEncoding.ToString().Trim();
                if (string.Equals(contentEncoding, "gzip", StringComparison.OrdinalIgnoreCase))
                {
                    gzipStream = new GZipStream(
                        Request.Body,
                        CompressionMode.Decompress,
                        leaveOpen: true);
                    input = gzipStream;
                }
                else if (string.IsNullOrEmpty(contentEncoding)
                    || string.Equals(contentEncoding, "identity", StringComparison.OrdinalIgnoreCase))
                {
                    input = Request.Body;
                }
                else
                {
                    return StatusCode(StatusCodes.Status415UnsupportedMediaType);
                }
            }

            await _transportBackend.ExecuteAsync(
                new GitTransportRequest(
                    repositoryContext,
                    operation.Service,
                    StatelessRpc: true,
                    operation.AdvertiseRefs,
                    protocolVersion,
                    principal.Identity?.Name ?? "anonymous"),
                input,
                Response.Body,
                timeoutSource.Token);
            await Response.Body.FlushAsync(timeoutSource.Token);
            return new EmptyResult();
        }
        catch (InvalidDataException)
        {
            return HandleTransportFailure(StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested
            && !HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Git {Service} timed out for repository {RepositoryName}.",
                operation.Service,
                repository.Name);
            return HandleTransportFailure(StatusCodes.Status504GatewayTimeout);
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
        catch (GitRepositoryNotFoundException)
        {
            return HandleTransportFailure(StatusCodes.Status404NotFound);
        }
        catch (GitTransportException exception)
        {
            _logger.LogError(
                exception,
                "Git {Service} failed for repository {RepositoryName}.",
                operation.Service,
                repository.Name);
            return HandleTransportFailure(StatusCodes.Status500InternalServerError);
        }
        finally
        {
            if (gzipStream is not null)
            {
                await gzipStream.DisposeAsync();
            }
        }
    }

    private static bool IsBrowserFallback(string? verb, string? service)
    {
        return !string.Equals(verb, "info/refs", StringComparison.Ordinal)
            && !string.Equals(verb, "git-upload-pack", StringComparison.Ordinal)
            && !string.Equals(verb, "git-receive-pack", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(service);
    }

    private static OperationResolution ResolveOperation(string? verb, string? service)
    {
        if (string.Equals(verb, "info/refs", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(service))
            {
                return new OperationResolution(null, StatusCodes.Status400BadRequest);
            }

            return service switch
            {
                "git-upload-pack" => new OperationResolution(
                    new GitSmartHttpOperation(
                        GitTransportService.UploadPack,
                        service,
                        HttpMethods.Get,
                        AdvertiseRefs: true),
                    null),
                "git-receive-pack" => new OperationResolution(
                    new GitSmartHttpOperation(
                        GitTransportService.ReceivePack,
                        service,
                        HttpMethods.Get,
                        AdvertiseRefs: true),
                    null),
                _ => new OperationResolution(null, StatusCodes.Status403Forbidden)
            };
        }

        return verb switch
        {
            "git-upload-pack" => new OperationResolution(
                new GitSmartHttpOperation(
                    GitTransportService.UploadPack,
                    verb,
                    HttpMethods.Post,
                    AdvertiseRefs: false),
                null),
            "git-receive-pack" => new OperationResolution(
                new GitSmartHttpOperation(
                    GitTransportService.ReceivePack,
                    verb,
                    HttpMethods.Post,
                    AdvertiseRefs: false),
                null),
            _ => new OperationResolution(null, StatusCodes.Status403Forbidden)
        };
    }

    private bool TryConfigureRequestBodyLimit()
    {
        if (Request.ContentLength > _options.MaxRequestBodySize)
        {
            return false;
        }

        var feature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = _options.MaxRequestBodySize;
        }

        return true;
    }

    private string? GetProtocolVersion()
    {
        var protocolVersion = Request.Headers[GitProtocolHeaderName].ToString().Trim();
        return protocolVersion is "version=1" or "version=2"
            ? protocolVersion
            : null;
    }

    private static bool UsesProtocolV2Advertisement(
        GitSmartHttpOperation operation,
        string? protocolVersion)
    {
        return operation.Service == GitTransportService.UploadPack
            && string.Equals(protocolVersion, "version=2", StringComparison.Ordinal);
    }

    private async Task WriteServiceAnnouncementAsync(
        string serviceName,
        CancellationToken cancellationToken)
    {
        var announcement = $"# service={serviceName}\n";
        var packet = $"{announcement.Length + 4:X4}{announcement}0000";
        await Response.Body.WriteAsync(
            Encoding.ASCII.GetBytes(packet),
            cancellationToken);
    }

    private async Task<IActionResult> ChallengeGitClientAsync()
    {
        await _authenticationService.ChallengeAsync(
            HttpContext,
            GitCandyAuthenticationSchemes.GitBasic,
            properties: null);
        return new EmptyResult();
    }

    private IActionResult HandleTransportFailure(int statusCode)
    {
        if (!Response.HasStarted)
        {
            return StatusCode(statusCode);
        }

        HttpContext.Abort();
        return new EmptyResult();
    }

    private void SetNoCacheHeaders()
    {
        Response.Headers[HeaderNames.Expires] = "Fri, 01 Jan 1980 00:00:00 GMT";
        Response.Headers[HeaderNames.Pragma] = "no-cache";
        Response.Headers[HeaderNames.CacheControl] = "no-cache, max-age=0, must-revalidate";
    }

    private sealed record GitSmartHttpOperation(
        GitTransportService Service,
        string ServiceName,
        string HttpMethod,
        bool AdvertiseRefs);

    private sealed record OperationResolution(
        GitSmartHttpOperation? Operation,
        int? ErrorStatusCode);
}
