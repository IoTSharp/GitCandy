using System.Globalization;
using System.Text.Json;
using GitCandy.Enterprise;
using Microsoft.AspNetCore.Http;

namespace GitCandy.Web.Enterprise;

/// <summary>企业微信 OAuth 与通讯录适配器。</summary>
public sealed class WeComEnterpriseProvider(IHttpClientFactory httpClientFactory)
    : IEnterpriseLoginProvider, IEnterpriseDirectoryProvider
{
    private const string DefaultApiBase = "https://qyapi.weixin.qq.com";
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public EnterpriseProviderKind Kind => EnterpriseProviderKind.WeCom;
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
            _ = await GetAccessTokenAsync(connection, secret, cancellationToken);
            return new EnterpriseProviderDiagnostic(true, "connected", "WeCom connection succeeded.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new EnterpriseProviderDiagnostic(false, "credential_rejected", "WeCom rejected the connection or its minimum contact scope is unavailable.");
        }
    }

    public Task<Uri> CreateAuthorizationUriAsync(
        EnterpriseConnectionContext connection,
        EnterpriseLoginChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        var agentId = GetConfigurationValue(connection.ConfigurationJson, "agentId")
            ?? throw new InvalidOperationException("WeCom requires agentId in non-secret configuration.");
        var query = QueryString.Create(new Dictionary<string, string?>
        {
            ["appid"] = connection.ExternalOrganizationId,
            ["agentid"] = agentId,
            ["redirect_uri"] = challenge.RedirectUri.AbsoluteUri,
            ["state"] = challenge.State,
            ["login_type"] = "CorpApp"
        });
        return Task.FromResult(new Uri("https://open.work.weixin.qq.com/wwopen/sso/qrConnect" + query));
    }

    public async Task<EnterpriseLoginIdentity?> RedeemAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        EnterpriseLoginCallback callback,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await GetAccessTokenAsync(connection, secret, cancellationToken);
        var uri = BuildApiUri(
            connection,
            "/cgi-bin/auth/getuserinfo",
            new Dictionary<string, string> { ["access_token"] = accessToken, ["code"] = callback.Code });
        using var document = await EnterpriseProviderHttp.SendJsonAsync(
            _httpClientFactory.CreateClient(MicrosoftEntraEnterpriseProvider.HttpClientName),
            new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken);
        var root = document.RootElement;
        if (!EnterpriseProviderHttp.IsSuccess(root)) return null;
        var externalId = EnterpriseProviderHttp.GetString(root, "userid")
            ?? EnterpriseProviderHttp.GetString(root, "open_userid");
        return string.IsNullOrWhiteSpace(externalId)
            ? null
            : new EnterpriseLoginIdentity(
                externalId,
                connection.ExternalOrganizationId,
                externalId,
                null,
                null);
    }

    public async Task<EnterpriseDirectoryPage> GetDirectoryPageAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            return new EnterpriseDirectoryPage([], [], null);
        }

        var accessToken = await GetAccessTokenAsync(connection, secret, cancellationToken);
        var client = _httpClientFactory.CreateClient(MicrosoftEntraEnterpriseProvider.HttpClientName);
        using var departmentDocument = await EnterpriseProviderHttp.SendJsonAsync(
            client,
            new HttpRequestMessage(HttpMethod.Get, BuildApiUri(
                connection,
                "/cgi-bin/department/list",
                new Dictionary<string, string> { ["access_token"] = accessToken })),
            cancellationToken);
        using var userDocument = await EnterpriseProviderHttp.SendJsonAsync(
            client,
            new HttpRequestMessage(HttpMethod.Get, BuildApiUri(
                connection,
                "/cgi-bin/user/list",
                new Dictionary<string, string>
                {
                    ["access_token"] = accessToken,
                    ["department_id"] = "1",
                    ["fetch_child"] = "1"
                })),
            cancellationToken);
        if (!EnterpriseProviderHttp.IsSuccess(departmentDocument.RootElement)
            || !EnterpriseProviderHttp.IsSuccess(userDocument.RootElement))
        {
            throw new HttpRequestException("WeCom directory request failed.");
        }

        var groups = departmentDocument.RootElement.TryGetProperty("department", out var departments)
            ? departments.EnumerateArray()
                .Select(item => new EnterpriseDirectoryGroup(
                    item.GetProperty("id").ToString(),
                    item.GetProperty("name").GetString() ?? item.GetProperty("id").ToString()))
                .ToArray()
            : [];
        var users = userDocument.RootElement.TryGetProperty("userlist", out var userList)
            ? userList.EnumerateArray().Select(item =>
            {
                var externalId = item.GetProperty("userid").GetString() ?? string.Empty;
                var groupIds = item.TryGetProperty("department", out var departmentIds)
                    ? departmentIds.EnumerateArray().Select(id => id.ToString()).ToArray()
                    : [];
                return new EnterpriseDirectoryUser(
                    externalId,
                    externalId,
                    item.TryGetProperty("email", out var email) ? email.GetString() : null,
                    item.TryGetProperty("name", out var name) ? name.GetString() : null,
                    !item.TryGetProperty("status", out var status) || status.GetInt32() != 4,
                    groupIds);
            }).Where(item => !string.IsNullOrWhiteSpace(item.ExternalId)).ToArray()
            : [];
        return new EnterpriseDirectoryPage(users, groups, null);
    }

    private async Task<string> GetAccessTokenAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        CancellationToken cancellationToken)
    {
        // WeCom mandates this credential exchange shape. The named client log category is pinned to Warning.
        var uri = BuildApiUri(
            connection,
            "/cgi-bin/gettoken",
            new Dictionary<string, string>
            {
                ["corpid"] = connection.ExternalOrganizationId,
                ["corpsecret"] = secret.Value
            });
        using var document = await EnterpriseProviderHttp.SendJsonAsync(
            _httpClientFactory.CreateClient(MicrosoftEntraEnterpriseProvider.HttpClientName),
            new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken);
        var root = document.RootElement;
        var token = EnterpriseProviderHttp.GetString(root, "access_token");
        if (!EnterpriseProviderHttp.IsSuccess(root) || string.IsNullOrWhiteSpace(token))
        {
            throw new HttpRequestException("WeCom token exchange failed.");
        }

        return token;
    }

    private static Uri BuildApiUri(
        EnterpriseConnectionContext connection,
        string path,
        IReadOnlyDictionary<string, string> query)
    {
        var apiBase = string.IsNullOrWhiteSpace(connection.ApiBaseUrl) ? DefaultApiBase : connection.ApiBaseUrl;
        return new Uri(
            apiBase.TrimEnd('/') + path + QueryString.Create(
                query.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))),
            UriKind.Absolute);
    }

    private static string? GetConfigurationValue(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }
}
