using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using GitCandy.Remotes;
using Microsoft.Extensions.Options;

namespace GitCandy.Web.Remotes;

internal sealed class GiteeRemoteRepositoryProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<RemoteProviderOptions> options)
    : HttpRemoteRepositoryProvider(httpClientFactory, options, RemoteProviderKind.Gitee)
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
        if (operations != RemoteRepositoryOperations.None)
        {
            scopes.Add("user_info");
        }

        if ((operations & (RemoteRepositoryOperations.Discover
                | RemoteRepositoryOperations.Import
                | RemoteRepositoryOperations.Pull
                | RemoteRepositoryOperations.Push)) != 0)
        {
            scopes.Add("projects");
        }

        return scopes;
    }

    protected override void ApplyAuthentication(
        HttpRequestMessage request,
        RemoteCredential credential) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("token", credential.Secret.Value);

    protected override RemoteAccountProfile ParseAccount(JsonElement root)
    {
        var externalId = RequiredId(root, "id");
        return new RemoteAccountProfile(
            CreateAccountIdentity(externalId),
            RemoteAccountKind.User,
            RequiredString(root, "login"),
            OptionalString(root, "name"));
    }

    protected override IReadOnlyList<RemoteRepositoryProfile> ParseRepositories(JsonElement root)
    {
        var repositories = new List<RemoteRepositoryProfile>();
        foreach (var item in root.EnumerateArray())
        {
            var fullName = RequiredString(item, "full_name");
            var separator = fullName.LastIndexOf('/');
            if (separator <= 0 || separator == fullName.Length - 1)
            {
                throw InvalidResponse();
            }

            var owner = fullName[..separator];
            var name = RequiredString(item, "path");
            repositories.Add(new RemoteRepositoryProfile(
                CreateRepositoryIdentity(RequiredId(item, "id")),
                owner,
                name,
                fullName,
                RequiredHttpsUri(item, "html_url"),
                OptionalBoolean(item, "private"),
                OptionalString(item, "default_branch")));
        }

        return repositories;
    }

    protected override string CreateRepositoriesPath(int pageNumber) =>
        $"user/repos?type=all&sort=updated&direction=desc&per_page=100&page={pageNumber.ToString(CultureInfo.InvariantCulture)}";
}
