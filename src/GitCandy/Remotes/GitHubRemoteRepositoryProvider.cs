using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using GitCandy.Remotes;
using Microsoft.Extensions.Options;

namespace GitCandy.Web.Remotes;

internal sealed class GitHubRemoteRepositoryProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<RemoteProviderOptions> options)
    : HttpRemoteRepositoryProvider(httpClientFactory, options, RemoteProviderKind.GitHub)
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
        RemoteRepositoryOperations operations) => operations == RemoteRepositoryOperations.None
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(["repo"], StringComparer.Ordinal);

    protected override void ApplyAuthentication(
        HttpRequestMessage request,
        RemoteCredential credential)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Secret.Value);
        request.Headers.UserAgent.ParseAdd("GitCandy/15");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    protected override RemoteAccountProfile ParseAccount(JsonElement root)
    {
        var externalId = RequiredId(root, "id");
        var login = RequiredString(root, "login");
        var kind = string.Equals(OptionalString(root, "type"), "Organization", StringComparison.OrdinalIgnoreCase)
            ? RemoteAccountKind.Organization
            : RemoteAccountKind.User;
        return new RemoteAccountProfile(
            CreateAccountIdentity(externalId),
            kind,
            login,
            OptionalString(root, "name"));
    }

    protected override IReadOnlyList<RemoteRepositoryProfile> ParseRepositories(JsonElement root)
    {
        var repositories = new List<RemoteRepositoryProfile>();
        foreach (var item in root.EnumerateArray())
        {
            var owner = item.TryGetProperty("owner", out var ownerElement)
                ? RequiredString(ownerElement, "login")
                : throw InvalidResponse();
            var name = RequiredString(item, "name");
            repositories.Add(new RemoteRepositoryProfile(
                CreateRepositoryIdentity(RequiredId(item, "id")),
                owner,
                name,
                OptionalString(item, "full_name") ?? $"{owner}/{name}",
                RequiredHttpsUri(item, "html_url"),
                OptionalBoolean(item, "private"),
                OptionalString(item, "default_branch")));
        }

        return repositories;
    }

    protected override string CreateRepositoriesPath(int pageNumber) =>
        $"user/repos?per_page=100&page={pageNumber.ToString(CultureInfo.InvariantCulture)}&affiliation=owner,collaborator,organization_member&sort=updated";
}
