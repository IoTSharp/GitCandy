using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GitCandy.Remotes;
using Microsoft.Extensions.Options;

namespace GitCandy.Web.Remotes;

internal abstract class HttpRemoteRepositoryProvider : IRemoteRepositoryProvider
{
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Uri _apiBaseUrl;

    protected HttpRemoteRepositoryProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<RemoteProviderOptions> options,
        RemoteProviderKind kind)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);

        _httpClientFactory = httpClientFactory;
        Kind = kind;
        var endpoint = options.Value.Get(kind);
        ServerUrl = new RemoteAccountIdentity(kind, endpoint.ServerUrl, "configuration").ServerUrl;
        _apiBaseUrl = NormalizeApiBaseUrl(endpoint.ApiBaseUrl);
    }

    public RemoteProviderKind Kind { get; }

    public Uri ServerUrl { get; }

    public RemoteProviderCapabilities Capabilities =>
        RemoteProviderCapabilities.AccountConnection
        | RemoteProviderCapabilities.RepositoryDiscovery;

    public abstract IReadOnlySet<RemoteAuthenticationKind> AuthenticationKinds { get; }

    protected abstract string AccountPath { get; }

    public abstract IReadOnlySet<string> GetRequiredScopes(
        RemoteAccountKind accountKind,
        RemoteRepositoryOperations operations);

    public async Task<RemoteProviderDiagnostic> TestAsync(
        RemoteAccountConnectionContext connection,
        RemoteCredential credential,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, AccountPath, credential);
        using var response = await SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode
            ? new RemoteProviderDiagnostic(true, "ok", $"Connected to {ProviderDisplayName(Kind)}.")
            : ToDiagnostic(response.StatusCode);
    }

    public async Task<RemoteAccountProfile?> GetAccountAsync(
        RemoteAccountConnectionContext connection,
        RemoteCredential credential,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, AccountPath, credential);
        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response.StatusCode);
        using var document = await ReadJsonAsync(response.Content, cancellationToken);
        return ParseAccount(document.RootElement);
    }

    public async Task<RemoteRepositoryPage> GetRepositoriesAsync(
        RemoteAccountConnectionContext connection,
        RemoteCredential credential,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = ParsePageCursor(cursor);
        using var request = CreateRequest(
            HttpMethod.Get,
            CreateRepositoriesPath(pageNumber),
            credential);
        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response.StatusCode);
        using var document = await ReadJsonAsync(response.Content, cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse();
        }

        var repositories = ParseRepositories(document.RootElement);
        var nextCursor = GetNextCursor(response, pageNumber, repositories.Count);
        return new RemoteRepositoryPage(repositories, nextCursor);
    }

    protected abstract void ApplyAuthentication(
        HttpRequestMessage request,
        RemoteCredential credential);

    protected abstract RemoteAccountProfile ParseAccount(JsonElement root);

    protected abstract IReadOnlyList<RemoteRepositoryProfile> ParseRepositories(JsonElement root);

    protected abstract string CreateRepositoriesPath(int pageNumber);

    protected virtual string? GetNextCursor(
        HttpResponseMessage response,
        int pageNumber,
        int repositoryCount)
    {
        if (response.Headers.TryGetValues("X-Next-Page", out var values))
        {
            var value = values.FirstOrDefault();
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var nextPage)
                && nextPage > pageNumber)
            {
                return nextPage.ToString(CultureInfo.InvariantCulture);
            }
        }

        return repositoryCount == 100
            ? (pageNumber + 1).ToString(CultureInfo.InvariantCulture)
            : null;
    }

    protected RemoteRepositoryIdentity CreateRepositoryIdentity(string externalId) =>
        new(Kind, ServerUrl.AbsoluteUri, externalId);

    protected RemoteAccountIdentity CreateAccountIdentity(string externalId) =>
        new(Kind, ServerUrl.AbsoluteUri, externalId);

    protected static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw InvalidResponse();
        }

        return property.GetString()!;
    }

    protected static string RequiredId(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            throw InvalidResponse();
        }

        return property.ValueKind switch
        {
            JsonValueKind.String when !string.IsNullOrWhiteSpace(property.GetString()) => property.GetString()!,
            JsonValueKind.Number when property.TryGetInt64(out var value) => value.ToString(CultureInfo.InvariantCulture),
            _ => throw InvalidResponse()
        };
    }

    protected static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    protected static bool OptionalBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();

    protected static Uri RequiredHttpsUri(JsonElement element, string propertyName)
    {
        var value = RequiredString(element, propertyName);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    && uri.IsLoopback)))
        {
            throw InvalidResponse();
        }

        return uri;
    }

    protected static RemoteProviderException InvalidResponse() => new(
        "invalid_response",
        "The remote provider returned an invalid response.");

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativePath,
        RemoteCredential credential)
    {
        var requestUri = new Uri(_apiBaseUrl, relativePath.TrimStart('/'));
        if (!string.Equals(requestUri.Scheme, _apiBaseUrl.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(requestUri.Host, _apiBaseUrl.Host, StringComparison.OrdinalIgnoreCase)
            || requestUri.Port != _apiBaseUrl.Port)
        {
            throw new InvalidOperationException("The remote provider request escaped its configured API origin.");
        }

        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyAuthentication(request, credential);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(RemoteServiceCollectionExtensions.HttpClientName);
            return await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new RemoteProviderException("timeout", "The remote provider request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new RemoteProviderException("network_error", "The remote provider could not be reached.");
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw InvalidResponse();
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > MaximumResponseBytes)
            {
                throw InvalidResponse();
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        destination.Position = 0;
        try
        {
            return await JsonDocument.ParseAsync(destination, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
    }

    private static int ParsePageCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return 1;
        }

        if (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var page)
            || page is < 1 or > 10000)
        {
            throw new RemoteProviderException("invalid_cursor", "The repository page cursor is invalid.");
        }

        return page;
    }

    private static void EnsureSuccess(HttpStatusCode statusCode)
    {
        if ((int)statusCode is >= 200 and < 300)
        {
            return;
        }

        var diagnostic = ToDiagnostic(statusCode);
        throw new RemoteProviderException(diagnostic.Code, diagnostic.Message);
    }

    private static RemoteProviderDiagnostic ToDiagnostic(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => new(false, "credential_rejected", "The remote provider rejected the credential."),
        HttpStatusCode.Forbidden => new(false, "access_forbidden", "The credential cannot access the requested provider resource."),
        HttpStatusCode.NotFound => new(false, "remote_not_found", "The configured provider endpoint was not found."),
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => new(false, "timeout", "The remote provider request timed out."),
        HttpStatusCode.TooManyRequests => new(false, "rate_limited", "The remote provider rate limit was reached."),
        >= HttpStatusCode.InternalServerError => new(false, "remote_unavailable", "The remote provider is temporarily unavailable."),
        >= HttpStatusCode.MultipleChoices and < HttpStatusCode.BadRequest => new(false, "redirect_rejected", "The remote provider redirected an authenticated API request."),
        _ => new(false, "provider_error", "The remote provider rejected the request.")
    };

    private static Uri NormalizeApiBaseUrl(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("A remote provider API URL must be HTTPS (or loopback HTTP) without credentials, query, or fragment.");
        }

        var builder = new UriBuilder(uri)
        {
            Path = $"{uri.AbsolutePath.TrimEnd('/')}/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static string ProviderDisplayName(RemoteProviderKind kind) => kind switch
    {
        RemoteProviderKind.GitHub => "GitHub",
        RemoteProviderKind.GitLab => "GitLab",
        RemoteProviderKind.Gitee => "Gitee",
        _ => kind.ToString()
    };
}
