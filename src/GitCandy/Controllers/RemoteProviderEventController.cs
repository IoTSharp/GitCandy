using GitCandy.Configuration;
using GitCandy.Remotes;
using GitCandy.Web.Remotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GitCandy.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting(ApiRateLimitPolicies.Write)]
[Route("remote-events/{connectionId:long}/{provider}")]
public sealed class RemoteProviderEventController(
    IRemoteProviderEventService eventService,
    IRemoteSecretResolver secretResolver,
    RemoteProviderWebhookSignatureValidator signatureValidator) : ControllerBase
{
    private const int MaxPayloadBytes = 1024 * 1024;
    private readonly IRemoteProviderEventService _eventService = eventService;
    private readonly IRemoteSecretResolver _secretResolver = secretResolver;
    private readonly RemoteProviderWebhookSignatureValidator _signatureValidator = signatureValidator;

    [HttpPost]
    [RequestSizeLimit(MaxPayloadBytes)]
    public async Task<IActionResult> Receive(
        long connectionId,
        string provider,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<RemoteProviderKind>(provider, ignoreCase: true, out var providerKind))
        {
            return NotFound();
        }

        var secretReference = await _eventService.GetWebhookSecretReferenceAsync(
            connectionId,
            providerKind,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            return NotFound();
        }
        var secret = await _secretResolver.ResolveAsync(secretReference, cancellationToken);
        if (secret is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var payload = await ReadBodyAsync(Request, cancellationToken);
        if (payload is null)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        if (!_signatureValidator.IsValid(providerKind, secret, Request.Headers, payload))
        {
            return Unauthorized();
        }

        var deliveryId = GetDeliveryId(providerKind, Request.Headers);
        var eventType = GetEventType(providerKind, Request.Headers);
        if (string.IsNullOrWhiteSpace(deliveryId) || string.IsNullOrWhiteSpace(eventType))
        {
            return BadRequest();
        }

        var result = await _eventService.ProcessAsync(
            new RemoteProviderEvent(connectionId, providerKind, deliveryId, eventType, payload),
            cancellationToken);
        return result.Accepted
            ? Ok(new { result.Code, result.Duplicate, result.EnqueuedMirrorCount })
            : BadRequest(new { result.Code });
    }

    private static async Task<byte[]?> ReadBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxPayloadBytes)
        {
            return null;
        }
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (output.Length + read > MaxPayloadBytes)
            {
                return null;
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private static string GetDeliveryId(RemoteProviderKind provider, IHeaderDictionary headers) =>
        provider switch
        {
            RemoteProviderKind.GitHub => headers["X-GitHub-Delivery"].ToString(),
            RemoteProviderKind.GitLab => First(headers, "X-Gitlab-Event-UUID", "X-Gitlab-Webhook-UUID"),
            RemoteProviderKind.Gitee => headers["X-Gitee-Delivery"].ToString(),
            _ => string.Empty
        };

    private static string GetEventType(RemoteProviderKind provider, IHeaderDictionary headers) =>
        provider switch
        {
            RemoteProviderKind.GitHub => headers["X-GitHub-Event"].ToString(),
            RemoteProviderKind.GitLab => headers["X-Gitlab-Event"].ToString(),
            RemoteProviderKind.Gitee => headers["X-Gitee-Event"].ToString(),
            _ => string.Empty
        };

    private static string First(IHeaderDictionary headers, string primary, string fallback)
    {
        var value = headers[primary].ToString();
        return string.IsNullOrWhiteSpace(value) ? headers[fallback].ToString() : value;
    }
}
