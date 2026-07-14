using System.Net;
using System.Security.Cryptography;
using System.Text;
using GitCandy.Enterprise;
using GitCandy.Web.Enterprise;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GitCandy.Tests;

[TestClass]
public sealed class EnterpriseLoginSecurityTests
{
    [TestMethod]
    public void Consume_WithProtectedStateAndMatchingCookie_ReturnsStateOnce()
    {
        var provider = DataProtectionProvider.Create(new DirectoryInfo(
            Path.Combine(Path.GetTempPath(), $"gitcandy-state-{Guid.NewGuid():N}")));
        var service = new EnterpriseLoginStateService(provider, TimeProvider.System);
        var beginContext = new DefaultHttpContext();
        var challenge = service.Create(
            beginContext.Response,
            42,
            "/me",
            "https://gitcandy.example/EnterpriseLogin/Callback");
        var setCookie = beginContext.Response.Headers.SetCookie.ToString();
        var cookie = setCookie.Split(';', 2)[0];
        var callbackContext = new DefaultHttpContext();
        callbackContext.Request.Headers.Cookie = cookie;

        var state = service.Consume(
            callbackContext.Request,
            callbackContext.Response,
            challenge.ProtectedState);

        Assert.IsNotNull(state);
        Assert.AreEqual(42, state.ConnectionId);
        Assert.AreEqual("/me", state.ReturnUrl);
        Assert.AreEqual(challenge.Nonce, state.Nonce);
        Assert.IsNull(service.Consume(
            new DefaultHttpContext().Request,
            new DefaultHttpContext().Response,
            challenge.ProtectedState));
    }

    [TestMethod]
    public async Task RedeemAsync_WithSignedTenantToken_ValidatesIssuerAudienceTenantAndNonce()
    {
        const string tenantId = "11111111-2222-3333-4444-555555555555";
        const string issuer = "https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/v2.0";
        using var rsa = RSA.Create(2048);
        var handler = new EntraFixtureHandler(rsa, issuer, tenantId, "expected-nonce");
        var provider = new MicrosoftEntraEnterpriseProvider(new FixedHttpClientFactory(handler));
        var connection = NewConnection(tenantId, issuer);

        var authorizationUri = await provider.CreateAuthorizationUriAsync(
            connection,
            new EnterpriseLoginChallenge(
                new Uri("https://gitcandy.example/EnterpriseLogin/Callback"),
                "protected-state",
                "expected-nonce",
                "pkce-challenge"));
        var identity = await provider.RedeemAsync(
            connection,
            new EnterpriseSecret("client-secret"),
            new EnterpriseLoginCallback(
                new Uri("https://gitcandy.example/EnterpriseLogin/Callback"),
                "authorization-code",
                "pkce-verifier",
                "expected-nonce"));

        StringAssert.Contains(authorizationUri.Query, "code_challenge=pkce-challenge");
        StringAssert.Contains(authorizationUri.Query, "state=protected-state");
        Assert.IsNotNull(identity);
        Assert.AreEqual("stable-object-id", identity.ExternalId);
        Assert.AreEqual(tenantId, identity.TenantId);
        Assert.AreEqual("person@example.com", identity.Email);
    }

    [TestMethod]
    public async Task RedeemAsync_WithWrongNonce_RejectsToken()
    {
        const string tenantId = "11111111-2222-3333-4444-555555555555";
        const string issuer = "https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/v2.0";
        using var rsa = RSA.Create(2048);
        var provider = new MicrosoftEntraEnterpriseProvider(
            new FixedHttpClientFactory(new EntraFixtureHandler(rsa, issuer, tenantId, "remote-nonce")));

        var identity = await provider.RedeemAsync(
            NewConnection(tenantId, issuer),
            new EnterpriseSecret("client-secret"),
            new EnterpriseLoginCallback(
                new Uri("https://gitcandy.example/EnterpriseLogin/Callback"),
                "authorization-code",
                "pkce-verifier",
                "different-nonce"));

        Assert.IsNull(identity);
    }

    private static EnterpriseConnectionContext NewConnection(string tenantId, string issuer) => new(
        42,
        7,
        "enterprise",
        "Entra",
        EnterpriseProviderKind.MicrosoftEntraId,
        tenantId,
        issuer,
        "client-id",
        "https://graph.microsoft.com/v1.0",
        "{\"allowJit\":true}",
        "env:ENTRA_SECRET",
        null,
        null,
        LoginEnabled: true,
        ProvisioningEnabled: true,
        IsEnabled: true);

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class EntraFixtureHandler : HttpMessageHandler
    {
        private readonly RSA _rsa;
        private readonly string _issuer;
        private readonly string _tenantId;
        private readonly string _nonce;

        public EntraFixtureHandler(RSA rsa, string issuer, string tenantId, string nonce)
        {
            _rsa = rsa;
            _issuer = issuer;
            _tenantId = tenantId;
            _nonce = nonce;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("openid-configuration", StringComparison.Ordinal))
            {
                return JsonAsync($$"""
                    {
                      "issuer": "{{_issuer}}",
                      "authorization_endpoint": "https://login.microsoftonline.com/{{_tenantId}}/oauth2/v2.0/authorize",
                      "token_endpoint": "https://login.microsoftonline.com/{{_tenantId}}/oauth2/v2.0/token",
                      "jwks_uri": "https://login.microsoftonline.com/{{_tenantId}}/discovery/v2.0/keys"
                    }
                    """);
            }

            if (path.EndsWith("/keys", StringComparison.Ordinal))
            {
                var parameters = _rsa.ExportParameters(includePrivateParameters: false);
                return JsonAsync($$"""
                    {"keys":[{"kty":"RSA","use":"sig","kid":"test-key","n":"{{WebEncoders.Base64UrlEncode(parameters.Modulus!)}}","e":"{{WebEncoders.Base64UrlEncode(parameters.Exponent!)}}"}]}
                    """);
            }

            if (path.EndsWith("/token", StringComparison.Ordinal))
            {
                var key = new RsaSecurityKey(_rsa) { KeyId = "test-key" };
                var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                {
                    Issuer = _issuer,
                    Audience = "client-id",
                    Expires = DateTime.UtcNow.AddMinutes(5),
                    NotBefore = DateTime.UtcNow.AddMinutes(-1),
                    SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
                    Claims = new Dictionary<string, object>
                    {
                        ["tid"] = _tenantId,
                        ["oid"] = "stable-object-id",
                        ["nonce"] = _nonce,
                        ["preferred_username"] = "person@example.com",
                        ["email"] = "person@example.com",
                        ["name"] = "Example Person"
                    }
                });
                return JsonAsync($$"""{"id_token":"{{token}}"}""");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> JsonAsync(string json) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }
}
