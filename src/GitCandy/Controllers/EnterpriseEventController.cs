using System.Security.Cryptography;
using System.Text.Json;
using GitCandy.Configuration;
using GitCandy.Enterprise;
using GitCandy.Web.Enterprise;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GitCandy.Controllers;

[ApiController]
[Route("enterprise-events/{connectionId:long}/{provider}")]
[AllowAnonymous]
[EnableRateLimiting(ApiRateLimitPolicies.Write)]
public sealed class EnterpriseEventController(
    IEnterpriseConnectionService connectionService,
    IEnterpriseSecretResolver secretResolver,
    IEnterpriseEventReceiptService receiptService,
    EnterpriseEventSignatureValidator signatureValidator) : ControllerBase
{
    private const int MaxPayloadBytes = 1024 * 1024;
    private readonly IEnterpriseConnectionService _connectionService = connectionService;
    private readonly IEnterpriseSecretResolver _secretResolver = secretResolver;
    private readonly IEnterpriseEventReceiptService _receiptService = receiptService;
    private readonly EnterpriseEventSignatureValidator _signatureValidator = signatureValidator;

    [HttpPost]
    [RequestSizeLimit(MaxPayloadBytes)]
    public async Task<IActionResult> Receive(
        long connectionId,
        string provider,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<EnterpriseProviderKind>(provider, ignoreCase: true, out var providerKind)
            || providerKind is not (EnterpriseProviderKind.Feishu or EnterpriseProviderKind.DingTalk))
        {
            return NotFound();
        }

        var connection = await _connectionService.GetRuntimeContextAsync(connectionId, cancellationToken);
        if (connection is null
            || !connection.IsEnabled
            || connection.Provider != providerKind
            || string.IsNullOrWhiteSpace(connection.WebhookSecretReference))
        {
            return NotFound();
        }

        var secret = await _secretResolver.ResolveAsync(
            connection.WebhookSecretReference,
            cancellationToken);
        if (secret is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var body = await ReadBodyAsync(Request, cancellationToken);
        if (body is null)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (!_signatureValidator.IsValid(providerKind, secret, Request.Headers, body))
        {
            return Unauthorized();
        }

        string? eventId;
        string? challenge = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            eventId = providerKind switch
            {
                EnterpriseProviderKind.Feishu => GetString(root, "header", "event_id")
                    ?? GetString(root, "event_id"),
                EnterpriseProviderKind.DingTalk => GetString(root, "eventId")
                    ?? Request.Headers["x-acs-dingtalk-event-id"].ToString(),
                _ => null
            };
            challenge = GetString(root, "challenge");
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        if (string.IsNullOrWhiteSpace(eventId))
        {
            eventId = challenge is null ? null : $"challenge:{Convert.ToHexString(SHA256.HashData(body))}";
        }

        if (string.IsNullOrWhiteSpace(eventId))
        {
            return BadRequest();
        }

        _ = await _receiptService.TryRecordAsync(
            connectionId,
            eventId,
            Convert.ToHexString(SHA256.HashData(body)),
            cancellationToken);
        return challenge is null ? Ok(new { code = 0 }) : Ok(new { challenge });
    }

    private static async Task<byte[]?> ReadBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxPayloadBytes) return null;
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxPayloadBytes) return null;
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static string? GetString(JsonElement element, params string[] path)
    {
        foreach (var segment in path)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty(segment, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
}
