namespace GitCandy.Credentials;

/// <summary>GitCandy Personal Access Token 的稳定 scope 名称。</summary>
public static class PersonalAccessTokenScopes
{
    public const string ApiRead = "api:read";
    public const string ApiWrite = "api:write";
    public const string GitRead = "git:read";
    public const string GitWrite = "git:write";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(
        [ApiRead, ApiWrite, GitRead, GitWrite],
        StringComparer.Ordinal);
}

/// <summary>供 UI 展示的 PAT 摘要，不包含 token hash 或明文。</summary>
public sealed record PersonalAccessTokenSummary(
    long Id,
    string Name,
    string TokenPrefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

/// <summary>只在创建响应中返回一次的 PAT。</summary>
public sealed record CreatedPersonalAccessToken(
    PersonalAccessTokenSummary Summary,
    string Token);

/// <summary>由有效 PAT 建立的机器身份。</summary>
public sealed record PersonalAccessTokenPrincipal(
    long CredentialId,
    string UserId,
    string UserName,
    bool IsAdministrator,
    IReadOnlySet<string> Scopes);

/// <summary>PAT 创建、查询、撤销和认证边界。</summary>
public interface IPersonalAccessTokenService
{
    Task<IReadOnlyList<PersonalAccessTokenSummary>> GetForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<CreatedPersonalAccessToken?> CreateAsync(
        string userId,
        string name,
        IEnumerable<string> scopes,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        string userId,
        long tokenId,
        CancellationToken cancellationToken = default);

    Task<PersonalAccessTokenPrincipal?> AuthenticateAsync(
        string token,
        CancellationToken cancellationToken = default);
}

/// <summary>机器凭据认证写入 ClaimsPrincipal 的稳定 claim 名称。</summary>
public static class CredentialClaimTypes
{
    public const string CredentialId = "gitcandy:credential:id";
    public const string CredentialKind = "gitcandy:credential:kind";
    public const string Scope = "gitcandy:credential:scope";
    public const string PersonalAccessToken = "pat";
    public const string DeployKey = "deploy-key";
    public const string UserSshKey = "user-key";
}
