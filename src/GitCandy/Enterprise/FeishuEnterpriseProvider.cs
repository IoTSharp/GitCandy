using System.Text.Json;
using GitCandy.Enterprise;
using Microsoft.AspNetCore.Http;

namespace GitCandy.Web.Enterprise;

/// <summary>飞书 tenant OAuth、登录和增量通讯录适配器。</summary>
public sealed class FeishuEnterpriseProvider(IHttpClientFactory httpClientFactory)
    : IEnterpriseLoginProvider, IEnterpriseDirectoryProvider
{
    private const string DefaultApiBase = "https://open.feishu.cn/open-apis";
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public EnterpriseProviderKind Kind => EnterpriseProviderKind.Feishu;
    public EnterpriseProviderCapabilities Capabilities =>
        EnterpriseProviderCapabilities.Login
        | EnterpriseProviderCapabilities.DirectoryUsers
        | EnterpriseProviderCapabilities.DirectoryGroups;

    public async Task<EnterpriseProviderDiagnostic> TestAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await GetTenantTokenAsync(connection, secret, cancellationToken);
            return new EnterpriseProviderDiagnostic(true, "connected", "Feishu tenant connection succeeded.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new EnterpriseProviderDiagnostic(false, "credential_rejected", "Feishu rejected the tenant credential or minimum contact scope.");
        }
    }

    public Task<Uri> CreateAuthorizationUriAsync(
        EnterpriseConnectionContext connection,
        EnterpriseLoginChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        EnsureClientId(connection);
        var query = QueryString.Create(new Dictionary<string, string?>
        {
            ["app_id"] = connection.ClientId,
            ["redirect_uri"] = challenge.RedirectUri.AbsoluteUri,
            ["state"] = challenge.State,
            ["scope"] = "contact:user.base:readonly",
            ["code_challenge"] = challenge.CodeChallenge,
            ["code_challenge_method"] = "S256"
        });
        return Task.FromResult(new Uri("https://accounts.feishu.cn/open-apis/authen/v1/authorize" + query));
    }

    public async Task<EnterpriseLoginIdentity?> RedeemAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        EnterpriseLoginCallback callback,
        CancellationToken cancellationToken = default)
    {
        EnsureClientId(connection);
        var tenantToken = await GetTenantTokenAsync(connection, secret, cancellationToken);
        var client = _httpClientFactory.CreateClient(MicrosoftEntraEnterpriseProvider.HttpClientName);
        using var tokenDocument = await EnterpriseProviderHttp.SendJsonAsync(
            client,
            EnterpriseProviderHttp.JsonPost(
                ApiUri(connection, "/authen/v2/oauth/token"),
                new
                {
                    grant_type = "authorization_code",
                    client_id = connection.ClientId,
                    client_secret = secret.Value,
                    code = callback.Code,
                    code_verifier = callback.CodeVerifier
                },
                tenantToken),
            cancellationToken);
        var userToken = EnterpriseProviderHttp.GetString(tokenDocument.RootElement, "access_token")
            ?? EnterpriseProviderHttp.GetString(tokenDocument.RootElement, "data", "access_token");
        if (string.IsNullOrWhiteSpace(userToken)) return null;
        using var userDocument = await EnterpriseProviderHttp.SendJsonAsync(
            client,
            new HttpRequestMessage(HttpMethod.Get, ApiUri(connection, "/authen/v1/user_info"))
            {
                Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken) }
            },
            cancellationToken);
        var root = userDocument.RootElement.TryGetProperty("data", out var data)
            ? data
            : userDocument.RootElement;
        var externalId = EnterpriseProviderHttp.GetString(root, "user_id")
            ?? EnterpriseProviderHttp.GetString(root, "union_id")
            ?? EnterpriseProviderHttp.GetString(root, "open_id");
        if (string.IsNullOrWhiteSpace(externalId)) return null;
        return new EnterpriseLoginIdentity(
            externalId,
            connection.ExternalOrganizationId,
            EnterpriseProviderHttp.GetString(root, "email") ?? externalId,
            EnterpriseProviderHttp.GetString(root, "email"),
            EnterpriseProviderHttp.GetString(root, "name"));
    }

    public async Task<EnterpriseDirectoryPage> GetDirectoryPageAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        var token = await GetTenantTokenAsync(connection, secret, cancellationToken);
        var pageToken = cursor ?? string.Empty;
        var client = _httpClientFactory.CreateClient(MicrosoftEntraEnterpriseProvider.HttpClientName);
        var usersUri = new Uri(ApiUri(connection, "/contact/v3/users/find_by_department").AbsoluteUri
            + QueryString.Create(new Dictionary<string, string?>
            {
                ["department_id"] = "0",
                ["department_id_type"] = "department_id",
                ["user_id_type"] = "user_id",
                ["page_size"] = "50",
                ["page_token"] = pageToken
            }));
        var departmentsUri = new Uri(ApiUri(connection, "/contact/v3/departments").AbsoluteUri
            + QueryString.Create(new Dictionary<string, string?>
            {
                ["parent_department_id"] = "0",
                ["department_id_type"] = "department_id",
                ["page_size"] = "50",
                ["page_token"] = pageToken
            }));
        using var userDocument = await SendBearerGetAsync(client, usersUri, token, cancellationToken);
        using var departmentDocument = await SendBearerGetAsync(client, departmentsUri, token, cancellationToken);
        var userData = userDocument.RootElement.GetProperty("data");
        var departmentData = departmentDocument.RootElement.GetProperty("data");
        var users = userData.TryGetProperty("items", out var items)
            ? items.EnumerateArray().Select(item =>
            {
                var externalId = EnterpriseProviderHttp.GetString(item, "user_id")
                    ?? EnterpriseProviderHttp.GetString(item, "open_id")
                    ?? string.Empty;
                var departments = item.TryGetProperty("department_ids", out var ids)
                    ? ids.EnumerateArray().Select(id => id.GetString() ?? string.Empty)
                        .Where(id => id.Length > 0).ToArray()
                    : [];
                return new EnterpriseDirectoryUser(
                    externalId,
                    EnterpriseProviderHttp.GetString(item, "email") ?? externalId,
                    EnterpriseProviderHttp.GetString(item, "email"),
                    EnterpriseProviderHttp.GetString(item, "name"),
                    !item.TryGetProperty("status", out var status)
                        || !status.TryGetProperty("is_frozen", out var frozen)
                        || !frozen.GetBoolean(),
                    departments);
            }).Where(item => item.ExternalId.Length > 0).ToArray()
            : [];
        var groups = departmentData.TryGetProperty("items", out var departmentItems)
            ? departmentItems.EnumerateArray().Select(item => new EnterpriseDirectoryGroup(
                EnterpriseProviderHttp.GetString(item, "department_id") ?? string.Empty,
                EnterpriseProviderHttp.GetString(item, "name") ?? string.Empty))
                .Where(item => item.ExternalId.Length > 0).ToArray()
            : [];
        var hasMore = userData.TryGetProperty("has_more", out var more) && more.GetBoolean();
        var nextCursor = hasMore ? EnterpriseProviderHttp.GetString(userData, "page_token") : null;
        return new EnterpriseDirectoryPage(users, groups, nextCursor);
    }

    private async Task<string> GetTenantTokenAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        CancellationToken cancellationToken)
    {
        EnsureClientId(connection);
        using var document = await EnterpriseProviderHttp.SendJsonAsync(
            _httpClientFactory.CreateClient(MicrosoftEntraEnterpriseProvider.HttpClientName),
            EnterpriseProviderHttp.JsonPost(
                ApiUri(connection, "/auth/v3/tenant_access_token/internal"),
                new { app_id = connection.ClientId, app_secret = secret.Value }),
            cancellationToken);
        var token = EnterpriseProviderHttp.GetString(document.RootElement, "tenant_access_token");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new HttpRequestException("Feishu tenant token exchange failed.");
        }

        return token;
    }

    private static async Task<JsonDocument> SendBearerGetAsync(
        HttpClient client,
        Uri uri,
        string token,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await EnterpriseProviderHttp.SendJsonAsync(client, request, cancellationToken);
    }

    private static Uri ApiUri(EnterpriseConnectionContext connection, string path)
    {
        var apiBase = string.IsNullOrWhiteSpace(connection.ApiBaseUrl) ? DefaultApiBase : connection.ApiBaseUrl;
        return new Uri(apiBase.TrimEnd('/') + path, UriKind.Absolute);
    }

    private static void EnsureClientId(EnterpriseConnectionContext connection)
    {
        if (string.IsNullOrWhiteSpace(connection.ClientId))
        {
            throw new InvalidOperationException("Feishu requires an app ID.");
        }
    }
}
