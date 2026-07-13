using System.Net;
using System.Security.Cryptography;
using System.Text;
using GitCandy.Configuration;
using GitCandy.Integrations;
using Microsoft.Extensions.Options;

namespace GitCandy.Web.Integrations;

/// <summary>发送带版本、delivery ID 和 HMAC-SHA256 签名的 webhook 请求。</summary>
public sealed class WebhookSender(
    IHttpClientFactory httpClientFactory,
    IWebhookSecretProtector secretProtector,
    IOptions<WebhookOptions> options) : IWebhookSender
{
    public const string HttpClientName = "GitCandy.Webhooks";
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IWebhookSecretProtector _secretProtector = secretProtector;
    private readonly WebhookOptions _options = options.Value;

    public async Task<WebhookSendResult> SendAsync(
        WebhookDeliveryWorkItem workItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        var payload = Encoding.UTF8.GetBytes(workItem.PayloadJson);
        string secret;
        try
        {
            secret = _secretProtector.Unprotect(workItem.ProtectedSecret);
        }
        catch (CryptographicException)
        {
            return new WebhookSendResult(false, ErrorCode: "secret_unprotect_failed");
        }
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload))
            .ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, workItem.TargetUrl)
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        request.Headers.TryAddWithoutValidation("X-GitCandy-Event", workItem.EventType);
        request.Headers.TryAddWithoutValidation("X-GitCandy-Delivery", workItem.DeliveryId);
        request.Headers.TryAddWithoutValidation("X-GitCandy-Webhook-Version", "1");
        request.Headers.TryAddWithoutValidation("X-GitCandy-Signature-256", $"sha256={signature}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            await DrainBoundedAsync(response, _options.MaxResponseBytes, timeout.Token);
            var statusCode = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new WebhookSendResult(true, statusCode)
                : new WebhookSendResult(false, statusCode, $"http_{statusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookSendResult(false, ErrorCode: "timeout");
        }
        catch (HttpRequestException)
        {
            return new WebhookSendResult(false, ErrorCode: "transport_error");
        }
    }

    private static async Task DrainBoundedAsync(
        HttpResponseMessage response,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content is null || maxBytes <= 0) return;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[Math.Min(maxBytes, 4096)];
        var remaining = maxBytes;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0) break;
            remaining -= read;
        }
    }
}
