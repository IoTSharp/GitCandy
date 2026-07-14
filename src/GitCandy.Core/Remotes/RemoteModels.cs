using System.Text.Json.Serialization;

namespace GitCandy.Remotes;

/// <summary>GitCandy 支持的远程 Git provider。</summary>
public enum RemoteProviderKind
{
    GitHub,
    GitLab,
    Gitee
}

/// <summary>远端账号的身份类型。</summary>
public enum RemoteAccountKind
{
    User,
    Organization,
    ServiceAccount
}

/// <summary>远程账号连接在 GitCandy 内的归属类型。</summary>
public enum RemoteConnectionOwnerKind
{
    User,
    Team
}

/// <summary>provider 支持的授权方式。</summary>
public enum RemoteAuthenticationKind
{
    OAuth,
    App,
    PersonalAccessToken,
    Ssh
}

/// <summary>远程账号连接的健康状态。</summary>
public enum RemoteConnectionStatus
{
    NotTested,
    Healthy,
    Failed,
    Revoked,
    Disabled
}

/// <summary>仓库镜像的数据流向；第一阶段只允许单向同步。</summary>
public enum RemoteMirrorDirection
{
    Pull,
    Push
}

/// <summary>仓库镜像中具有最终解释权的一侧。</summary>
public enum RemoteMirrorAuthority
{
    GitCandy,
    Remote
}

/// <summary>仓库镜像选择 Git refs 的方式。</summary>
public enum RemoteMirrorRefFilterKind
{
    AllRefs,
    ProtectedBranches,
    AllowList,
    RegularExpression
}

/// <summary>检测到目标 ref 与权威侧分叉时的处理策略。</summary>
public enum RemoteMirrorDivergencePolicy
{
    Stop,
    KeepDivergent,
    OverwriteTarget
}

/// <summary>仓库镜像最近一次可观测运行状态。</summary>
public enum RemoteMirrorStatus
{
    Pending,
    Idle,
    Synchronizing,
    Succeeded,
    Diverged,
    Failed,
    Paused
}

/// <summary>远程 provider 可提供的能力。</summary>
[Flags]
public enum RemoteProviderCapabilities
{
    None = 0,
    AccountConnection = 1,
    RepositoryDiscovery = 2,
    RepositoryImport = 4,
    PullMirror = 8,
    PushMirror = 16,
    WebhookEvents = 32
}

/// <summary>用于计算 provider 最小授权 scope 的远程操作。</summary>
[Flags]
public enum RemoteRepositoryOperations
{
    None = 0,
    Discover = 1,
    Import = 2,
    Pull = 4,
    Push = 8,
    Webhook = 16
}

/// <summary>GitCandy 内部的连接归属，不包含 provider 凭据。</summary>
public sealed record RemoteConnectionOwner
{
    public RemoteConnectionOwner(RemoteConnectionOwnerKind kind, string stableId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableId);

        Kind = kind;
        StableId = stableId.Trim();
    }

    public RemoteConnectionOwnerKind Kind { get; }

    public string StableId { get; }
}

/// <summary>不随远端账号登录名或显示名变化的稳定身份。</summary>
public sealed record RemoteAccountIdentity
{
    public RemoteAccountIdentity(
        RemoteProviderKind provider,
        string serverUrl,
        string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        Provider = provider;
        ServerUrl = RemoteIdentityRules.NormalizeServerUrl(serverUrl);
        ExternalId = externalId.Trim();
    }

    public RemoteProviderKind Provider { get; }

    public Uri ServerUrl { get; }

    public string ExternalId { get; }
}

/// <summary>远端账号的可变展示资料。</summary>
public sealed record RemoteAccountProfile(
    RemoteAccountIdentity Identity,
    RemoteAccountKind Kind,
    string Login,
    string? DisplayName);

/// <summary>不随仓库路径、owner 登录名或仓库名变化的稳定远端仓库身份。</summary>
public sealed record RemoteRepositoryIdentity
{
    public RemoteRepositoryIdentity(
        RemoteProviderKind provider,
        string serverUrl,
        string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        Provider = provider;
        ServerUrl = RemoteIdentityRules.NormalizeServerUrl(serverUrl);
        ExternalId = externalId.Trim();
    }

    public RemoteProviderKind Provider { get; }

    public Uri ServerUrl { get; }

    public string ExternalId { get; }
}

/// <summary>仓库发现返回的非敏感远端资料。</summary>
public sealed record RemoteRepositoryProfile(
    RemoteRepositoryIdentity Identity,
    string OwnerLogin,
    string Name,
    string FullName,
    Uri WebUrl,
    bool IsPrivate,
    string? DefaultBranch);

/// <summary>有界远端仓库发现页。</summary>
public sealed record RemoteRepositoryPage(
    IReadOnlyList<RemoteRepositoryProfile> Repositories,
    string? NextCursor);

/// <summary>不回显 provider 响应或 secret 的连接诊断。</summary>
public sealed record RemoteProviderDiagnostic(
    bool Succeeded,
    string Code,
    string Message);

/// <summary>opaque secret store 引用；它不是 token、密码或私钥本身。</summary>
public sealed record RemoteSecretReference
{
    public RemoteSecretReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 512
            || !normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "A remote secret reference must be an opaque, scheme-qualified reference.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>只在受控 provider/vault 边界中存在的远程凭据 secret。</summary>
public sealed class RemoteSecret
{
    public RemoteSecret(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    [JsonIgnore]
    public string Value { get; }

    public override string ToString() => "[REDACTED]";
}

/// <summary>运行时解析出的远程凭据；不得持久化、记录或返回到 MVC 模型。</summary>
public sealed class RemoteCredential
{
    public RemoteCredential(
        RemoteAuthenticationKind authenticationKind,
        RemoteSecret secret,
        IEnumerable<string> grantedScopes,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(grantedScopes);

        AuthenticationKind = authenticationKind;
        Secret = secret;
        GrantedScopes = RemoteScopePolicy.Normalize(grantedScopes);
        ExpiresAt = expiresAt;
    }

    public RemoteAuthenticationKind AuthenticationKind { get; }

    [JsonIgnore]
    public RemoteSecret Secret { get; }

    public IReadOnlySet<string> GrantedScopes { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public override string ToString() =>
        $"RemoteCredential {{ AuthenticationKind = {AuthenticationKind}, Secret = [REDACTED], ScopeCount = {GrantedScopes.Count} }}";
}

/// <summary>可持久化和展示的远程凭据元数据，不包含 secret。</summary>
public sealed record RemoteCredentialMetadata(
    RemoteSecretReference Reference,
    RemoteAuthenticationKind AuthenticationKind,
    IReadOnlySet<string> GrantedScopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);

/// <summary>远程账号连接的 provider 运行上下文，不包含凭据明文。</summary>
public sealed record RemoteAccountConnectionContext(
    long ConnectionId,
    RemoteConnectionOwner Owner,
    RemoteAccountProfile Account,
    RemoteAuthenticationKind AuthenticationKind,
    RemoteSecretReference CredentialReference,
    IReadOnlySet<string> GrantedScopes,
    bool IsEnabled);

/// <summary>scope 校验结果。</summary>
public sealed record RemoteScopeValidation(
    bool Satisfied,
    IReadOnlyList<string> MissingScopes);

/// <summary>provider scope 规范化和最小权限校验。</summary>
public static class RemoteScopePolicy
{
    public static RemoteScopeValidation Validate(
        IEnumerable<string> grantedScopes,
        IEnumerable<string> requiredScopes)
    {
        var granted = Normalize(grantedScopes);
        var required = Normalize(requiredScopes);
        var missing = required
            .Where(scope => !granted.Contains(scope))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new RemoteScopeValidation(missing.Length == 0, missing);
    }

    internal static IReadOnlySet<string> Normalize(IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in scopes)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException("Remote credential scopes cannot contain empty values.", nameof(scopes));
            }

            normalized.Add(scope.Trim());
        }

        return normalized;
    }
}

/// <summary>远程凭据 secret store 的创建、解析、轮换和撤销边界。</summary>
public interface IRemoteCredentialVault
{
    Task<RemoteCredentialMetadata> StoreAsync(
        RemoteConnectionOwner owner,
        RemoteCredential credential,
        CancellationToken cancellationToken = default);

    ValueTask<RemoteCredential?> ResolveAsync(
        RemoteSecretReference reference,
        CancellationToken cancellationToken = default);

    Task<RemoteCredentialMetadata?> RotateAsync(
        RemoteSecretReference reference,
        RemoteCredential replacement,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        RemoteSecretReference reference,
        CancellationToken cancellationToken = default);
}

/// <summary>GitHub、GitLab 或 Gitee 远程账号与仓库发现适配边界。</summary>
public interface IRemoteRepositoryProvider
{
    RemoteProviderKind Kind { get; }

    RemoteProviderCapabilities Capabilities { get; }

    IReadOnlySet<RemoteAuthenticationKind> AuthenticationKinds { get; }

    IReadOnlySet<string> GetRequiredScopes(
        RemoteAccountKind accountKind,
        RemoteRepositoryOperations operations);

    Task<RemoteProviderDiagnostic> TestAsync(
        RemoteAccountConnectionContext connection,
        RemoteCredential credential,
        CancellationToken cancellationToken = default);

    Task<RemoteAccountProfile?> GetAccountAsync(
        RemoteAccountConnectionContext connection,
        RemoteCredential credential,
        CancellationToken cancellationToken = default);

    Task<RemoteRepositoryPage> GetRepositoriesAsync(
        RemoteAccountConnectionContext connection,
        RemoteCredential credential,
        string? cursor,
        CancellationToken cancellationToken = default);
}

/// <summary>已注册远程 provider 的唯一查找入口。</summary>
public interface IRemoteProviderCatalog
{
    IReadOnlyList<RemoteProviderKind> AvailableProviders { get; }

    IRemoteRepositoryProvider? Get(RemoteProviderKind kind);
}

internal static class RemoteIdentityRules
{
    public static Uri NormalizeServerUrl(string serverUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        if (!Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var uri)
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !(string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    && uri.IsLoopback))
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "A remote server URL must be HTTPS (or loopback HTTP) without credentials, query, or fragment.",
                nameof(serverUrl));
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/'),
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }
}
