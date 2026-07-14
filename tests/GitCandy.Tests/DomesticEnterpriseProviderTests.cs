using System.Net;
using System.Security.Cryptography;
using System.Text;
using GitCandy.Enterprise;
using GitCandy.Web.Enterprise;
using Microsoft.AspNetCore.Http;

namespace GitCandy.Tests;

[TestClass]
public sealed class DomesticEnterpriseProviderTests
{
    [TestMethod]
    public async Task WeCom_WithDirectoryAndLogin_UsesStableUserIdAndDepartmentIds()
    {
        var handler = new RoutingHandler(async request =>
        {
            await Task.Yield();
            var path = request.RequestUri?.AbsolutePath;
            return path switch
            {
                "/cgi-bin/gettoken" => Json("{\"errcode\":0,\"access_token\":\"wecom-token\"}"),
                "/cgi-bin/department/list" => Json("{\"errcode\":0,\"department\":[{\"id\":1,\"name\":\"Root\"},{\"id\":7,\"name\":\"Platform\"}]}"),
                "/cgi-bin/user/list" => Json("{\"errcode\":0,\"userlist\":[{\"userid\":\"wx-user-7\",\"name\":\"Wei Xin\",\"email\":\"wx@example.com\",\"department\":[7],\"status\":1}]}"),
                "/cgi-bin/auth/getuserinfo" => Json("{\"errcode\":0,\"userid\":\"wx-user-7\"}"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var provider = new WeComEnterpriseProvider(new FixedHttpClientFactory(handler));
        var connection = NewConnection(EnterpriseProviderKind.WeCom, "corp-1", "wecom-app", "{\"agentId\":\"100001\"}");

        var directory = await provider.GetDirectoryPageAsync(
            connection,
            new EnterpriseSecret("wecom-secret"),
            null);
        var identity = await provider.RedeemAsync(
            connection,
            new EnterpriseSecret("wecom-secret"),
            Callback());

        Assert.HasCount(1, directory.Users);
        Assert.AreEqual("wx-user-7", directory.Users[0].ExternalId);
        CollectionAssert.AreEqual(new[] { "7" }, directory.Users[0].GroupExternalIds.ToArray());
        Assert.AreEqual("wx-user-7", identity?.ExternalId);
    }

    [TestMethod]
    public async Task Feishu_WithIncrementalPage_UsesTenantUserIdAndRefreshesSecretPerCall()
    {
        var observedBodies = new List<string>();
        var handler = new RoutingHandler(async request =>
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
            if (request.RequestUri?.AbsolutePath.EndsWith("tenant_access_token/internal", StringComparison.Ordinal) == true)
            {
                observedBodies.Add(body);
                return Json("{\"tenant_access_token\":\"tenant-token\"}");
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("find_by_department", StringComparison.Ordinal) == true)
            {
                return Json("{\"data\":{\"items\":[{\"user_id\":\"fs-user-1\",\"name\":\"Fei Shu\",\"email\":\"fs@example.com\",\"department_ids\":[\"d1\"],\"status\":{\"is_frozen\":false}}],\"has_more\":true,\"page_token\":\"next-page\"}}");
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/contact/v3/departments", StringComparison.Ordinal) == true)
            {
                return Json("{\"data\":{\"items\":[{\"department_id\":\"d1\",\"name\":\"Platform\"}]}}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var provider = new FeishuEnterpriseProvider(new FixedHttpClientFactory(handler));
        var connection = NewConnection(EnterpriseProviderKind.Feishu, "tenant-feishu", "cli_feishu", null);

        var first = await provider.GetDirectoryPageAsync(connection, new EnterpriseSecret("secret-v1"), null);
        _ = await provider.GetDirectoryPageAsync(connection, new EnterpriseSecret("secret-v2"), "next-page");

        Assert.AreEqual("fs-user-1", first.Users[0].ExternalId);
        Assert.AreEqual("next-page", first.NextCursor);
        Assert.IsTrue(observedBodies.Any(body => body.Contains("secret-v1", StringComparison.Ordinal)));
        Assert.IsTrue(observedBodies.Any(body => body.Contains("secret-v2", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task DingTalk_WithOAuthIdentity_PrefersUnionIdAndScopesUserIdToTenant()
    {
        var handler = new RoutingHandler(async request =>
        {
            await Task.Yield();
            var path = request.RequestUri?.AbsolutePath;
            return path switch
            {
                "/v1.0/oauth2/userAccessToken" => Json("{\"accessToken\":\"user-token\"}"),
                "/v1.0/contact/users/me" => Json("{\"unionId\":\"union-stable\",\"userId\":\"tenant-user\",\"nick\":\"Ding User\",\"email\":\"ding@example.com\"}"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var provider = new DingTalkEnterpriseProvider(new FixedHttpClientFactory(handler));
        var connection = NewConnection(EnterpriseProviderKind.DingTalk, "corp-ding", "ding-app-key", null);

        var identity = await provider.RedeemAsync(
            connection,
            new EnterpriseSecret("ding-secret"),
            Callback());

        Assert.IsNotNull(identity);
        Assert.AreEqual("union-stable", identity.ExternalId);
        Assert.AreEqual("corp-ding", identity.TenantId);
        Assert.AreEqual("ding@example.com", identity.Email);
    }

    [TestMethod]
    public void IsValid_WithFeishuAndDingTalkSignatures_RejectsTamperingAndStaleTimestamp()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var validator = new EnterpriseEventSignatureValidator(new FixedTimeProvider(now));
        var body = Encoding.UTF8.GetBytes("{\"eventId\":\"event-1\"}");
        var feishuSecret = new EnterpriseSecret("feishu-signing-secret");
        var feishuHeaders = new HeaderDictionary
        {
            ["X-Lark-Request-Timestamp"] = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["X-Lark-Request-Nonce"] = "nonce-1"
        };
        feishuHeaders["X-Lark-Signature"] = CreateFeishuSignature(
            feishuHeaders["X-Lark-Request-Timestamp"].ToString(),
            "nonce-1",
            feishuSecret.Value,
            body);
        Assert.IsTrue(validator.IsValid(EnterpriseProviderKind.Feishu, feishuSecret, feishuHeaders, body));
        Assert.IsFalse(validator.IsValid(EnterpriseProviderKind.Feishu, feishuSecret, feishuHeaders, [.. body, 0]));

        var dingSecret = new EnterpriseSecret("ding-signing-secret");
        var dingTimestamp = now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var dingHeaders = new HeaderDictionary
        {
            ["x-acs-dingtalk-timestamp"] = dingTimestamp,
            ["x-acs-dingtalk-signature"] = CreateDingTalkSignature(dingTimestamp, dingSecret.Value, body)
        };
        Assert.IsTrue(validator.IsValid(EnterpriseProviderKind.DingTalk, dingSecret, dingHeaders, body));
        dingHeaders["x-acs-dingtalk-timestamp"] = now.AddMinutes(-10).ToUnixTimeMilliseconds()
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsFalse(validator.IsValid(EnterpriseProviderKind.DingTalk, dingSecret, dingHeaders, body));
    }

    private static EnterpriseConnectionContext NewConnection(
        EnterpriseProviderKind provider,
        string organizationId,
        string clientId,
        string? configurationJson) => new(
        1,
        2,
        "enterprise",
        provider.ToString(),
        provider,
        organizationId,
        null,
        clientId,
        "https://provider.example",
        configurationJson,
        "env:PROVIDER_SECRET",
        null,
        null,
        LoginEnabled: true,
        ProvisioningEnabled: true,
        IsEnabled: true);

    private static EnterpriseLoginCallback Callback() => new(
        new Uri("https://gitcandy.example/EnterpriseLogin/Callback"),
        "code",
        "verifier",
        "nonce");

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string CreateFeishuSignature(
        string timestamp,
        string nonce,
        string secret,
        byte[] body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(timestamp));
        hash.AppendData(Encoding.UTF8.GetBytes(nonce));
        hash.AppendData(Encoding.UTF8.GetBytes(secret));
        hash.AppendData(body);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string CreateDingTalkSignature(string timestamp, string secret, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash([
            .. Encoding.UTF8.GetBytes(timestamp + "\n"),
            .. body
        ]));
    }

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class RoutingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> route) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _route = route;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _route(request);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
