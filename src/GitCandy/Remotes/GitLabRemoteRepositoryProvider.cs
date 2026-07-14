using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using GitCandy.Remotes;
using Microsoft.Extensions.Options;

namespace GitCandy.Web.Remotes;

internal sealed class GitLabRemoteRepositoryProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<RemoteProviderOptions> options)
    : HttpRemoteRepositoryProvider(httpClientFactory, options, RemoteProviderKind.GitLab)
{
    private static readonly IReadOnlySet<RemoteAuthenticationKind> SupportedAuthenticationKinds =
        new HashSet<RemoteAuthenticationKind>
        {
            RemoteAuthenticationKind.OAuth,
            RemoteAuthenticationKind.PersonalAccessToken
        };

    public override IReadOnlySet<RemoteAuthenticationKind> AuthenticationKinds =>
        SupportedAuthenticationKinds;

    protected override string AccountPath => "user";

    public override IReadOnlySet<string> GetRequiredScopes(
        RemoteAccountKind accountKind,
        RemoteRepositoryOperations operations)
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        if ((operations & RemoteRepositoryOperations.Discover) != 0)
        {
            scopes.Add("read_api");
        }

        if ((operations & (RemoteRepositoryOperations.Import | RemoteRepositoryOperations.Pull)) != 0)
        {
            scopes.Add("read_repository");
        }

        if ((operations & (RemoteRepositoryOperations.Push | RemoteRepositoryOperations.Webhook)) != 0)
        {
            scopes.Add("api");
        }

        return scopes;
    }

    protected override void ApplyAuthentication(
        HttpRequestMessage request,
        RemoteCredential credential)
    {
        if (credential.AuthenticationKind == RemoteAuthenticationKind.PersonalAccessToken)
        {
            request.Headers.Add("PRIVATE-TOKEN", credential.Secret.Value);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Secret.Value);
        }
    }

    protected override RemoteAccountProfile ParseAccount(JsonElement root)
    {
        var externalId = RequiredId(root, "id");
        return new RemoteAccountProfile(
            CreateAccountIdentity(externalId),
            RemoteAccountKind.User,
            RequiredString(root, "username"),
            OptionalString(root, "name"));
    }

    protected override IReadOnlyList<RemoteRepositoryProfile> ParseRepositories(JsonElement root)
    {
        var repositories = new List<RemoteRepositoryProfile>();
        foreach (var item in root.EnumerateArray())
        {
            var fullName = RequiredString(item, "path_with_namespace");
            var separator = fullName.LastIndexOf('/');
            if (separator <= 0 || separator == fullName.Length - 1)
            {
                throw InvalidResponse();
            }

            var owner = fullName[..separator];
            var name = RequiredString(item, "path");
            var visibility = OptionalString(item, "visibility");
            repositories.Add(new RemoteRepositoryProfile(
                CreateRepositoryIdentity(RequiredId(item, "id")),
                owner,
                name,
                fullName,
                RequiredHttpsUri(item, "web_url"),
                !string.Equals(visibility, "public", StringComparison.OrdinalIgnoreCase),
                OptionalString(item, "default_branch")));
        }

        return repositories;
    }

    protected override string CreateRepositoriesPath(int pageNumber) =>
        $"projects?membership=true&simple=true&per_page=100&page={pageNumber.ToString(CultureInfo.InvariantCulture)}&order_by=last_activity_at&sort=desc";
}
