using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GitCandy.Enterprise;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GitCandy.Web.Enterprise;

/// <summary>Microsoft Entra ID OIDC 与 Graph 连接适配器。</summary>
public sealed class MicrosoftEntraEnterpriseProvider(IHttpClientFactory httpClientFactory)
    : IEnterpriseLoginProvider
{
    public const string HttpClientName = "GitCandy.EnterpriseProviders";
    private const int MaxDocumentBytes = 512 * 1024;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public EnterpriseProviderKind Kind => EnterpriseProviderKind.MicrosoftEntraId;

    public EnterpriseProviderCapabilities Capabilities =>
        EnterpriseProviderCapabilities.Login
        | EnterpriseProviderCapabilities.DirectoryUsers
        | EnterpriseProviderCapabilities.DirectoryGroups;

    public async Task<EnterpriseProviderDiagnostic> TestAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateConnection(connection, out var error))
        {
            return new EnterpriseProviderDiagnostic(false, "configuration_invalid", error);
        }

        try
        {
            var configuration = await GetConfigurationAsync(connection, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = connection.ClientId!,
                    ["client_secret"] = secret.Value,
                    ["grant_type"] = "client_credentials",
                    ["scope"] = "https://graph.microsoft.com/.default"
                })
            };
            using var response = await _httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new EnterpriseProviderDiagnostic(false, "credential_rejected", "Microsoft Entra ID rejected the configured client credential.");
            }

            _ = await ReadBoundedStringAsync(response.Content, cancellationToken);
            return new EnterpriseProviderDiagnostic(true, "connected", "Microsoft Entra ID connection succeeded.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new EnterpriseProviderDiagnostic(false, "metadata_unavailable", "Microsoft Entra ID metadata or token endpoint could not be validated.");
        }
    }

    public async Task<Uri> CreateAuthorizationUriAsync(
        EnterpriseConnectionContext connection,
        EnterpriseLoginChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateConnection(connection, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var configuration = await GetConfigurationAsync(connection, cancellationToken);
        var query = QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = connection.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = challenge.RedirectUri.AbsoluteUri,
            ["response_mode"] = "query",
            ["scope"] = "openid profile email",
            ["state"] = challenge.State,
            ["nonce"] = challenge.Nonce,
            ["code_challenge"] = challenge.CodeChallenge,
            ["code_challenge_method"] = "S256"
        });
        return new Uri(configuration.AuthorizationEndpoint + query, UriKind.Absolute);
    }

    public async Task<EnterpriseLoginIdentity?> RedeemAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        EnterpriseLoginCallback callback,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateConnection(connection, out _))
        {
            return null;
        }

        var configuration = await GetConfigurationAsync(connection, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = connection.ClientId!,
                ["client_secret"] = secret.Value,
                ["grant_type"] = "authorization_code",
                ["code"] = callback.Code,
                ["redirect_uri"] = callback.RedirectUri.AbsoluteUri,
                ["code_verifier"] = callback.CodeVerifier
            })
        };
        using var response = await _httpClientFactory.CreateClient(HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var tokenJson = await ReadBoundedStringAsync(response.Content, cancellationToken);
        using var tokenDocument = JsonDocument.Parse(tokenJson);
        if (!tokenDocument.RootElement.TryGetProperty("id_token", out var idTokenProperty)
            || idTokenProperty.GetString() is not string idToken
            || string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            idToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = configuration.Issuer,
                ValidateAudience = true,
                ValidAudience = connection.ClientId,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                IssuerSigningKeys = configuration.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(2)
            });
        if (!validation.IsValid || validation.ClaimsIdentity is null)
        {
            return null;
        }

        var identity = validation.ClaimsIdentity;
        var tenantId = identity.FindFirst("tid")?.Value;
        var nonce = identity.FindFirst("nonce")?.Value;
        var externalId = identity.FindFirst("oid")?.Value ?? identity.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId)
            || !string.Equals(tenantId, connection.ExternalOrganizationId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(nonce, callback.ExpectedNonce, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        var userName = identity.FindFirst("preferred_username")?.Value
            ?? identity.FindFirst(ClaimTypes.Upn)?.Value
            ?? identity.FindFirst("email")?.Value
            ?? externalId;
        return new EnterpriseLoginIdentity(
            externalId,
            tenantId,
            userName,
            identity.FindFirst("email")?.Value ?? identity.FindFirst("preferred_username")?.Value,
            identity.FindFirst("name")?.Value);
    }

    private async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        EnterpriseConnectionContext connection,
        CancellationToken cancellationToken)
    {
        var authority = new Uri(connection.Authority!, UriKind.Absolute);
        var metadataUri = new Uri(
            authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration",
            UriKind.Absolute);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var metadataResponse = await client.GetAsync(
            metadataUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        metadataResponse.EnsureSuccessStatusCode();
        var configuration = OpenIdConnectConfiguration.Create(
            await ReadBoundedStringAsync(metadataResponse.Content, cancellationToken));
        if (!IsHttpsEndpoint(configuration.AuthorizationEndpoint)
            || !IsHttpsEndpoint(configuration.TokenEndpoint)
            || !IsHttpsEndpoint(configuration.JwksUri)
            || !IssuerMatchesTenant(configuration.Issuer, connection.ExternalOrganizationId))
        {
            throw new SecurityTokenInvalidIssuerException("The Entra metadata issuer or endpoints do not match the configured tenant.");
        }

        using var keysResponse = await client.GetAsync(
            configuration.JwksUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        keysResponse.EnsureSuccessStatusCode();
        var keySet = new JsonWebKeySet(await ReadBoundedStringAsync(keysResponse.Content, cancellationToken));
        foreach (var key in keySet.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        if (configuration.SigningKeys.Count == 0)
        {
            throw new SecurityTokenInvalidSigningKeyException("The Entra metadata contains no signing keys.");
        }

        return configuration;
    }

    private static bool TryValidateConnection(
        EnterpriseConnectionContext connection,
        out string error)
    {
        if (connection.Provider != EnterpriseProviderKind.MicrosoftEntraId
            || string.IsNullOrWhiteSpace(connection.ClientId)
            || string.IsNullOrWhiteSpace(connection.ExternalOrganizationId)
            || !Uri.TryCreate(connection.Authority, UriKind.Absolute, out var authority)
            || !string.Equals(authority.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Microsoft Entra ID requires an HTTPS authority, client ID, and tenant ID.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool IssuerMatchesTenant(string issuer, string tenantId) =>
        Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri)
        && string.Equals(issuerUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && issuerUri.Segments.Any(segment => string.Equals(
            segment.Trim('/'),
            tenantId,
            StringComparison.OrdinalIgnoreCase));

    private static bool IsHttpsEndpoint(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ReadBoundedStringAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxDocumentBytes)
        {
            throw new HttpRequestException("The provider response exceeded the configured limit.");
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > MaxDocumentBytes)
            {
                throw new HttpRequestException("The provider response exceeded the configured limit.");
            }

            output.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }
}
