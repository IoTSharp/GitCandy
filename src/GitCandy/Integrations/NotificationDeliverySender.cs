using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitCandy.Configuration;
using GitCandy.Integrations;
using GitCandy.Notifications;
using Microsoft.Extensions.Options;

namespace GitCandy.Web.Integrations;

/// <summary>通过已配置 SMTP 或受 SSRF 约束的签名 webhook 投递用户通知。</summary>
public sealed class NotificationDeliverySender(
    IHttpClientFactory httpClientFactory,
    IWebhookSecretProtector secretProtector,
    IOptions<WebhookOptions> webhookOptions,
    IOptions<GitCandyIdentityOptions> identityOptions,
    ILogger<NotificationDeliverySender> logger) : INotificationDeliverySender
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IWebhookSecretProtector _secretProtector = secretProtector;
    private readonly WebhookOptions _webhookOptions = webhookOptions.Value;
    private readonly GitCandySmtpOptions _smtpOptions = identityOptions.Value.AccountRecovery.Smtp;
    private readonly ILogger<NotificationDeliverySender> _logger = logger;

    public Task<NotificationDeliveryResult> SendAsync(
        NotificationDeliveryWorkItem workItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return workItem.Channel switch
        {
            NotificationDeliveryChannel.Email => SendEmailAsync(workItem, cancellationToken),
            NotificationDeliveryChannel.Webhook => SendWebhookAsync(workItem, cancellationToken),
            _ => Task.FromResult(new NotificationDeliveryResult(false, ErrorCode: "unsupported_channel"))
        };
    }

    private async Task<NotificationDeliveryResult> SendEmailAsync(
        NotificationDeliveryWorkItem workItem,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_smtpOptions.Host)
            || string.IsNullOrWhiteSpace(_smtpOptions.FromAddress))
        {
            return new NotificationDeliveryResult(false, ErrorCode: "smtp_not_configured");
        }
        using var message = new MailMessage(
            _smtpOptions.FromAddress,
            workItem.Recipient,
            $"[GitCandy] {workItem.Title}",
            $"{workItem.Title}\r\n\r\n{workItem.Url}\r\n");
        using var client = new SmtpClient(_smtpOptions.Host, _smtpOptions.Port)
        {
            EnableSsl = _smtpOptions.EnableSsl
        };
        if (!string.IsNullOrWhiteSpace(_smtpOptions.UserName))
        {
            client.Credentials = new NetworkCredential(_smtpOptions.UserName, _smtpOptions.Password);
        }
        try
        {
            await client.SendMailAsync(message, cancellationToken);
            return new NotificationDeliveryResult(true);
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(exception, "Notification email delivery {DeliveryId} failed.", workItem.DeliveryId);
            return new NotificationDeliveryResult(false, ErrorCode: "smtp_error");
        }
    }

    private async Task<NotificationDeliveryResult> SendWebhookAsync(
        NotificationDeliveryWorkItem workItem,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workItem.ProtectedSecret))
        {
            return new NotificationDeliveryResult(false, ErrorCode: "secret_missing");
        }
        string secret;
        try
        {
            secret = _secretProtector.Unprotect(workItem.ProtectedSecret);
        }
        catch (CryptographicException)
        {
            return new NotificationDeliveryResult(false, ErrorCode: "secret_unprotect_failed");
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            type = workItem.EventType.ToString().ToLowerInvariant(),
            notification = new { title = workItem.Title, url = workItem.Url }
        });
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload))
            .ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, workItem.Recipient)
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        request.Headers.TryAddWithoutValidation("X-GitCandy-Event", $"notification.{workItem.EventType.ToString().ToLowerInvariant()}");
        request.Headers.TryAddWithoutValidation("X-GitCandy-Delivery", workItem.DeliveryId);
        request.Headers.TryAddWithoutValidation("X-GitCandy-Webhook-Version", "1");
        request.Headers.TryAddWithoutValidation("X-GitCandy-Signature-256", $"sha256={signature}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_webhookOptions.RequestTimeout);
        try
        {
            using var response = await _httpClientFactory.CreateClient(WebhookSender.HttpClientName).SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            var statusCode = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new NotificationDeliveryResult(true, statusCode)
                : new NotificationDeliveryResult(false, statusCode, $"http_{statusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NotificationDeliveryResult(false, ErrorCode: "timeout");
        }
        catch (HttpRequestException)
        {
            return new NotificationDeliveryResult(false, ErrorCode: "transport_error");
        }
    }
}
