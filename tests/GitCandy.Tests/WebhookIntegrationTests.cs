using System.Net;
using System.Security.Cryptography;
using System.Text;
using GitCandy.Configuration;
using GitCandy.Integrations;
using GitCandy.Web.Integrations;
using GitCandy.Notifications;
using GitCandy.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GitCandy.Tests;

[TestClass]
public sealed class WebhookIntegrationTests
{
    [TestMethod]
    public async Task OutboundTargetPolicy_WithDefaultPolicy_BlocksHttpAndPrivateAddressesAtSaveAndConnect()
    {
        var policy = new OutboundTargetPolicy(Options.Create(new WebhookOptions()));

        Assert.IsFalse(await policy.IsAllowedAsync(new Uri("http://8.8.8.8/hook")));
        Assert.IsFalse(await policy.IsAllowedAsync(new Uri("https://127.0.0.1/hook")));
        Assert.IsFalse(await policy.IsAllowedAsync(new Uri("https://10.0.0.1/hook")));
        Assert.IsTrue(await policy.IsAllowedAsync(new Uri("https://8.8.8.8/hook")));
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await policy.ConnectAsync(new DnsEndPoint("127.0.0.1", 80)));
    }

    [TestMethod]
    public async Task WebhookSender_WithSuccessfulReceiver_SendsVersionDeliveryAndValidHmacWithoutSecret()
    {
        const string secret = "whsec_fixture-secret";
        const string payload = "{\"version\":1,\"type\":\"push\"}";
        var handler = new CapturingHandler();
        var sender = new WebhookSender(
            new TestHttpClientFactory(handler),
            new TestSecretProtector(secret),
            Options.Create(new WebhookOptions { RequestTimeout = TimeSpan.FromSeconds(5) }));
        var workItem = new WebhookDeliveryWorkItem(
            "0123456789abcdef0123456789abcdef",
            new string('a', 64),
            "push",
            new Uri("https://ci.example.test/hooks"),
            "protected-secret",
            payload,
            AttemptCount: 1);

        var result = await sender.SendAsync(workItem);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(204, result.ResponseStatusCode);
        Assert.AreEqual("push", handler.EventType);
        Assert.AreEqual(workItem.DeliveryId, handler.DeliveryId);
        Assert.AreEqual("1", handler.Version);
        Assert.AreEqual(payload, handler.Payload);
        Assert.IsFalse(handler.Payload.Contains(secret, StringComparison.Ordinal));
        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        Assert.AreEqual(expected, handler.Signature);
    }

    [TestMethod]
    public async Task WebhookSender_WithAllowedLoopbackFixture_DeliversThroughValidatedSocket()
    {
        var received = new TaskCompletionSource<(string DeliveryId, string Payload)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var receiver = builder.Build();
        receiver.MapPost("/hook", async context =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var payload = await reader.ReadToEndAsync(context.RequestAborted);
            received.TrySetResult((context.Request.Headers["X-GitCandy-Delivery"].ToString(), payload));
            context.Response.StatusCode = StatusCodes.Status202Accepted;
        });
        await receiver.StartAsync();
        try
        {
            var address = receiver.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses.Single();
            Assert.IsNotNull(address);
            var options = Options.Create(new WebhookOptions
            {
                AllowHttpTargets = true,
                AllowPrivateNetworkTargets = true,
                RequestTimeout = TimeSpan.FromSeconds(5)
            });
            var policy = new OutboundTargetPolicy(options);
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                ConnectCallback = (context, cancellationToken) =>
                    policy.ConnectAsync(context.DnsEndPoint, cancellationToken)
            };
            var sender = new WebhookSender(
                new TestHttpClientFactory(handler),
                new TestSecretProtector("whsec_loopback"),
                options);
            const string deliveryId = "abcdef0123456789abcdef0123456789";
            const string payload = "{\"version\":1,\"type\":\"check.updated\"}";

            var result = await sender.SendAsync(new WebhookDeliveryWorkItem(
                deliveryId,
                new string('b', 64),
                "check.updated",
                new Uri($"{address}/hook"),
                "protected-secret",
                payload,
                AttemptCount: 1));
            var captured = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(202, result.ResponseStatusCode);
            Assert.AreEqual(deliveryId, captured.DeliveryId);
            Assert.AreEqual(payload, captured.Payload);
        }
        finally
        {
            await receiver.StopAsync();
            await receiver.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task NotificationDeliverySender_WithWebhookPreference_SignsBoundedVersionedPayload()
    {
        const string secret = "notification-secret";
        var handler = new CapturingHandler();
        var sender = new NotificationDeliverySender(
            new TestHttpClientFactory(handler),
            new TestSecretProtector(secret),
            Options.Create(new WebhookOptions { RequestTimeout = TimeSpan.FromSeconds(5) }),
            Options.Create(new GitCandyIdentityOptions()),
            NullLogger<NotificationDeliverySender>.Instance);
        var result = await sender.SendAsync(new NotificationDeliveryWorkItem(
            "abcdef0123456789abcdef0123456789",
            NotificationDeliveryChannel.Webhook,
            "https://notifications.example.test/hook",
            "protected-secret",
            WorkspaceNotificationEventType.Check,
            "Check ci/build: Success",
            "/owner/repository/pulls/1",
            AttemptCount: 1));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("notification.check", handler.EventType);
        StringAssert.Contains(handler.Payload, "\"version\":1");
        StringAssert.Contains(handler.Payload, "Check ci/build: Success");
        Assert.IsFalse(handler.Payload.Contains(secret, StringComparison.Ordinal));
        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(handler.Payload))).ToLowerInvariant();
        Assert.AreEqual(expected, handler.Signature);

        var email = await sender.SendAsync(new NotificationDeliveryWorkItem(
            "0123456789abcdef0123456789abcdef",
            NotificationDeliveryChannel.Email,
            "owner@example.com",
            null,
            WorkspaceNotificationEventType.Release,
            "Release published",
            "/owner/repository/releases/1",
            AttemptCount: 1));
        Assert.IsFalse(email.Succeeded);
        Assert.AreEqual("smtp_not_configured", email.ErrorCode);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? EventType { get; private set; }
        public string? DeliveryId { get; private set; }
        public string? Version { get; private set; }
        public string? Signature { get; private set; }
        public string Payload { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            EventType = request.Headers.GetValues("X-GitCandy-Event").Single();
            DeliveryId = request.Headers.GetValues("X-GitCandy-Delivery").Single();
            Version = request.Headers.GetValues("X-GitCandy-Webhook-Version").Single();
            Signature = request.Headers.GetValues("X-GitCandy-Signature-256").Single();
            var content = request.Content ?? throw new InvalidOperationException("Webhook request content is required.");
            Payload = await content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class TestSecretProtector(string secret) : IWebhookSecretProtector
    {
        private readonly string _secret = secret;

        public string Protect(string value) => "protected-secret";
        public string Unprotect(string protectedSecret) => _secret;
    }
}
