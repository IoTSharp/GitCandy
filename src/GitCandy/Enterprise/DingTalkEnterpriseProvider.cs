using GitCandy.Enterprise;
using Microsoft.AspNetCore.Http;

namespace GitCandy.Web.Enterprise;

/// <summary>钉钉 OAuth、unionId/userId 和通讯录适配器。</summary>
public sealed class DingTalkEnterpriseProvider(IHttpClientFactory httpClientFactory)
    : IEnterpriseLoginProvider, IEnterpriseDirectoryProvider
{
    private const string DefaultApiBase = "https://api.dingtalk.com";
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public EnterpriseProviderKind Kind => EnterpriseProviderKind.DingTalk;
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
            _ = await GetAppTokenAsync(connection, secret, cancellationToken);
            return new EnterpriseProviderDiagnostic(true, "connected", "DingTalk organization connection succeeded.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new EnterpriseProviderDiagnostic(false, "credential_rejected", "DingTalk rejected the application credential or contact scope.");
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
            ["redirect_uri"] = challenge.RedirectUri.AbsoluteUri,
            ["response_type"] = "code",
            ["client_id"] = connection.ClientId,
            ["scope"] = "openid",
            ["state"] = challenge.State,
            ["prompt"] = "consent",
            ["code_challenge"] = challenge.CodeChallenge,
            ["code_challenge_method"] = "S256"
        });
        return Task.FromResult(new Uri("https://login.dingtalk.com/oauth2/auth" + query));
    }

    public async Task<EnterpriseLoginIdentity?> RedeemAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        EnterpriseLoginCallback callback,
        CancellationToken cancellationToken = default)
    {
        EnsureClientId(connection);
        var client = _httpClientFactory.CreateClient(MicrosoftEntraEnterpriseProvider.HttpClientName);
        using var tokenDocument = await EnterpriseProviderHttp.SendJsonAsync(
            client,
            EnterpriseProviderHttp.JsonPost(
                ApiUri(connection, "/v1.0/oauth2/userAccessToken"),
                new
                {
                    clientId = connection.ClientId,
                    clientSecret = secret.Value,
                    code = callback.Code,
                    grantType = "authorization_code",
                    codeVerifier = callback.CodeVerifier
                }),
            cancellationToken);
        var userToken = EnterpriseProviderHttp.GetString(tokenDocument.RootElement, "accessToken");
        if (string.IsNullOrWhiteSpace(userToken)) return null;
        using var userDocument = await EnterpriseProviderHttp.SendJsonAsync(
            client,
            new HttpRequestMessage(HttpMethod.Get, ApiUri(connection, "/v1.0/contact/users/me"))
            {
                Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken) }
            },
            cancellationToken);
        var root = userDocument.RootElement;
        var unionId = EnterpriseProviderHttp.GetString(root, "unionId");
        var userId = EnterpriseProviderHttp.GetString(root, "userId");
        var externalId = !string.IsNullOrWhiteSpace(unionId)
            ? unionId
            : userId;
        if (string.IsNullOrWhiteSpace(externalId)) return null;
        return new EnterpriseLoginIdentity(
            externalId,
            connection.ExternalOrganizationId,
            EnterpriseProviderHttp.GetString(root, "email") ?? userId ?? externalId,
            EnterpriseProviderHttp.GetString(root, "email"),
            EnterpriseProviderHttp.GetString(root, "nick"));
    }

    public async Task<EnterpriseDirectoryPage> GetDirectoryPageAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAppTokenAsync(connection, secret, cancellationToken);
        var client = _httpClientFactory.CreateClient(MicrosoftEntraEnterpriseProvider.HttpClientName);
        using var departmentDocument = await EnterpriseProviderHttp.SendJsonAsync(
            client,
            EnterpriseProviderHttp.JsonPost(
                ApiUri(connection, "/v1.0/contact/departments/search"),
                new { parentId = 1, maxResults = 100, nextToken = cursor },
                token),
            cancellationToken);
        using var userDocument = await EnterpriseProviderHttp.SendJsonAsync(
            client,
            EnterpriseProviderHttp.JsonPost(
                ApiUri(connection, "/v1.0/contact/users/search"),
                new { deptId = 1, cursor = ParseCursor(cursor), size = 100, containAccessLimit = false },
                token),
            cancellationToken);
        var groups = departmentDocument.RootElement.TryGetProperty("departments", out var departments)
            ? departments.EnumerateArray().Select(item => new EnterpriseDirectoryGroup(
                EnterpriseProviderHttp.GetString(item, "deptId") ?? string.Empty,
                EnterpriseProviderHttp.GetString(item, "name") ?? string.Empty))
                .Where(item => item.ExternalId.Length > 0).ToArray()
            : [];
        var users = userDocument.RootElement.TryGetProperty("list", out var list)
            ? list.EnumerateArray().Select(item =>
            {
                var userId = EnterpriseProviderHttp.GetString(item, "userid")
                    ?? EnterpriseProviderHttp.GetString(item, "userId")
                    ?? string.Empty;
                var unionId = EnterpriseProviderHttp.GetString(item, "unionid")
                    ?? EnterpriseProviderHttp.GetString(item, "unionId");
                var departmentsForUser = item.TryGetProperty("dept_id_list", out var ids)
                    ? ids.EnumerateArray().Select(id => id.ToString()).ToArray()
                    : [];
                return new EnterpriseDirectoryUser(
                    unionId ?? userId,
                    userId,
                    EnterpriseProviderHttp.GetString(item, "email"),
                    EnterpriseProviderHttp.GetString(item, "name"),
                    !item.TryGetProperty("active", out var active) || active.GetBoolean(),
                    departmentsForUser);
            }).Where(item => item.ExternalId.Length > 0).ToArray()
            : [];
        var hasMore = userDocument.RootElement.TryGetProperty("has_more", out var more) && more.GetBoolean();
        var nextCursor = hasMore
            ? EnterpriseProviderHttp.GetString(userDocument.RootElement, "next_cursor")
            : null;
        return new EnterpriseDirectoryPage(users, groups, nextCursor);
    }

    private async Task<string> GetAppTokenAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        CancellationToken cancellationToken)
    {
        EnsureClientId(connection);
        using var document = await EnterpriseProviderHttp.SendJsonAsync(
            _httpClientFactory.CreateClient(MicrosoftEntraEnterpriseProvider.HttpClientName),
            EnterpriseProviderHttp.JsonPost(
                ApiUri(connection, "/v1.0/oauth2/accessToken"),
                new { appKey = connection.ClientId, appSecret = secret.Value }),
            cancellationToken);
        var token = EnterpriseProviderHttp.GetString(document.RootElement, "accessToken");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new HttpRequestException("DingTalk app token exchange failed.");
        }

        return token;
    }

    private static long ParseCursor(string? cursor) =>
        long.TryParse(cursor, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static Uri ApiUri(EnterpriseConnectionContext connection, string path)
    {
        var apiBase = string.IsNullOrWhiteSpace(connection.ApiBaseUrl) ? DefaultApiBase : connection.ApiBaseUrl;
        return new Uri(apiBase.TrimEnd('/') + path, UriKind.Absolute);
    }

    private static void EnsureClientId(EnterpriseConnectionContext connection)
    {
        if (string.IsNullOrWhiteSpace(connection.ClientId))
        {
            throw new InvalidOperationException("DingTalk requires an app key.");
        }
    }
}
