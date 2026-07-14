namespace GitCandy.Enterprise;

/// <summary>GitCandy 支持的企业身份提供方。</summary>
public enum EnterpriseProviderKind
{
    MicrosoftEntraId,
    Scim,
    WeCom,
    Feishu,
    DingTalk
}

/// <summary>企业连接提供的能力。</summary>
[Flags]
public enum EnterpriseProviderCapabilities
{
    None = 0,
    Login = 1,
    DirectoryUsers = 2,
    DirectoryGroups = 4,
    ProvisioningEndpoint = 8
}

/// <summary>企业连接最近一次诊断或同步状态。</summary>
public enum EnterpriseConnectionStatus
{
    NotTested,
    Healthy,
    Degraded,
    Failed,
    Disabled
}

/// <summary>创建或更新企业连接时允许持久化的非敏感配置。</summary>
public sealed record EnterpriseConnectionEdit(
    long? Id,
    string Name,
    EnterpriseProviderKind Provider,
    string ExternalOrganizationId,
    string? Authority,
    string? ClientId,
    string? ApiBaseUrl,
    string? ConfigurationJson,
    string SecretReference,
    string? WebhookSecretReference,
    bool LoginEnabled,
    bool ProvisioningEnabled,
    bool IsEnabled);

/// <summary>不包含 secret 的企业连接摘要。</summary>
public sealed record EnterpriseConnectionSummary(
    long Id,
    string TeamName,
    string Name,
    EnterpriseProviderKind Provider,
    string ExternalOrganizationId,
    string? Authority,
    string? ClientId,
    string? ApiBaseUrl,
    string? ConfigurationJson,
    string SecretReference,
    string? WebhookSecretReference,
    bool LoginEnabled,
    bool ProvisioningEnabled,
    bool IsEnabled,
    EnterpriseConnectionStatus Status,
    string? LastErrorCode,
    DateTimeOffset? LastTestedAt,
    DateTimeOffset? LastSynchronizedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>provider 执行所需的非敏感连接上下文。</summary>
public sealed record EnterpriseConnectionContext(
    long Id,
    long TeamId,
    string TeamName,
    string Name,
    EnterpriseProviderKind Provider,
    string ExternalOrganizationId,
    string? Authority,
    string? ClientId,
    string? ApiBaseUrl,
    string? ConfigurationJson,
    string SecretReference,
    string? WebhookSecretReference,
    string? SyncCursor,
    bool LoginEnabled,
    bool ProvisioningEnabled,
    bool IsEnabled);

/// <summary>不回显远端响应或 secret 的连接诊断。</summary>
public sealed record EnterpriseProviderDiagnostic(
    bool Succeeded,
    string Code,
    string Message);

/// <summary>登录页可公开展示的已启用企业连接。</summary>
public sealed record EnterpriseLoginOption(
    long ConnectionId,
    string TeamName,
    string ConnectionName,
    EnterpriseProviderKind Provider);

/// <summary>OIDC/OAuth 授权请求的安全参数。</summary>
public sealed record EnterpriseLoginChallenge(
    Uri RedirectUri,
    string State,
    string Nonce,
    string CodeChallenge);

/// <summary>OIDC/OAuth 回调兑换输入。</summary>
public sealed record EnterpriseLoginCallback(
    Uri RedirectUri,
    string Code,
    string CodeVerifier,
    string ExpectedNonce);

/// <summary>经过 provider 验签和 tenant 校验的稳定外部身份。</summary>
public sealed record EnterpriseLoginIdentity(
    string ExternalId,
    string TenantId,
    string UserName,
    string? Email,
    string? DisplayName);

/// <summary>企业登录绑定结果。</summary>
public enum EnterpriseSignInStatus
{
    Succeeded,
    NotProvisioned,
    Conflict,
    Disabled,
    InvalidIdentity
}

/// <summary>企业登录绑定服务结果。</summary>
public sealed record EnterpriseSignInResult(
    EnterpriseSignInStatus Status,
    string? UserId = null,
    string? ErrorCode = null);

/// <summary>新生成的 SCIM bearer；明文只返回一次。</summary>
public sealed record CreatedScimBearer(
    long ConnectionId,
    string TeamName,
    string Token,
    string Prefix,
    DateTimeOffset CreatedAt);

/// <summary>SCIM bearer 生成和验证边界。</summary>
public interface IScimBearerService
{
    Task<CreatedScimBearer?> RotateAsync(
        long connectionId,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default);

    Task<long?> ValidateAsync(
        string token,
        CancellationToken cancellationToken = default);
}

/// <summary>SCIM 用户写入值。</summary>
public sealed record ScimUserData(
    string ExternalId,
    string UserName,
    string? Email,
    string? DisplayName,
    bool Active);

/// <summary>SCIM 用户资源投影。</summary>
public sealed record ScimUserResource(
    long Id,
    string ExternalId,
    string UserName,
    string? Email,
    string? DisplayName,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt);

/// <summary>SCIM Group 写入值。</summary>
public sealed record ScimGroupData(
    string ExternalId,
    string DisplayName,
    IReadOnlyList<long> MemberIds);

/// <summary>SCIM Group 资源投影。</summary>
public sealed record ScimGroupResource(
    long Id,
    string ExternalId,
    string DisplayName,
    IReadOnlyList<long> MemberIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt);

/// <summary>SCIM 分页结果。</summary>
public sealed record ScimPage<T>(
    int TotalResults,
    int StartIndex,
    int ItemsPerPage,
    IReadOnlyList<T> Resources);

/// <summary>SCIM 写入结果。</summary>
public sealed record ScimWriteResult<T>(
    bool Succeeded,
    bool Created,
    T? Resource,
    string? ErrorCode = null);

/// <summary>目录对账停用结果。</summary>
public sealed record EnterpriseDeprovisionResult(
    int Deactivated,
    int ProtectedOwners,
    int Failed);

/// <summary>单个连接的目录同步结果。</summary>
public sealed record EnterpriseDirectorySyncResult(
    long ConnectionId,
    bool Succeeded,
    int UsersProcessed,
    int GroupsProcessed,
    int UsersDeactivated,
    string? ErrorCode = null);

/// <summary>SCIM Users/Groups 持久化与 Identity 生命周期边界。</summary>
public interface IScimProvisioningService
{
    Task<ScimPage<ScimUserResource>> GetUsersAsync(
        long connectionId,
        int startIndex,
        int count,
        string? filterAttribute,
        string? filterValue,
        CancellationToken cancellationToken = default);

    Task<ScimUserResource?> GetUserAsync(
        long connectionId,
        long resourceId,
        CancellationToken cancellationToken = default);

    Task<ScimWriteResult<ScimUserResource>> UpsertUserAsync(
        long connectionId,
        ScimUserData user,
        CancellationToken cancellationToken = default);

    Task<ScimWriteResult<ScimUserResource>> PatchUserAsync(
        long connectionId,
        long resourceId,
        ScimUserData user,
        CancellationToken cancellationToken = default);

    Task<ScimPage<ScimGroupResource>> GetGroupsAsync(
        long connectionId,
        int startIndex,
        int count,
        string? filterAttribute,
        string? filterValue,
        CancellationToken cancellationToken = default);

    Task<ScimGroupResource?> GetGroupAsync(
        long connectionId,
        long resourceId,
        CancellationToken cancellationToken = default);

    Task<ScimWriteResult<ScimGroupResource>> UpsertGroupAsync(
        long connectionId,
        ScimGroupData group,
        CancellationToken cancellationToken = default);

    Task<ScimWriteResult<ScimGroupResource>> PatchGroupAsync(
        long connectionId,
        long resourceId,
        ScimGroupData group,
        CancellationToken cancellationToken = default);

    Task<EnterpriseDeprovisionResult> DeactivateMissingUsersAsync(
        long connectionId,
        IReadOnlyCollection<string> activeExternalIds,
        CancellationToken cancellationToken = default);
}

/// <summary>企业目录主动同步与对账入口。</summary>
public interface IEnterpriseDirectorySyncService
{
    Task<IReadOnlyList<EnterpriseDirectorySyncResult>> SynchronizeAllAsync(
        CancellationToken cancellationToken = default);

    Task<EnterpriseDirectorySyncResult> SynchronizeAsync(
        long connectionId,
        CancellationToken cancellationToken = default);
}

/// <summary>运行时解析出的 secret；不得持久化、记录或返回到 MVC 模型。</summary>
public sealed record EnterpriseSecret(string Value);

/// <summary>企业 secret store 的引用解析边界。</summary>
public interface IEnterpriseSecretResolver
{
    ValueTask<EnterpriseSecret?> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken = default);
}

/// <summary>企业 provider 的共同诊断入口。</summary>
public interface IEnterpriseProvider
{
    EnterpriseProviderKind Kind { get; }

    EnterpriseProviderCapabilities Capabilities { get; }

    Task<EnterpriseProviderDiagnostic> TestAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        CancellationToken cancellationToken = default);
}

/// <summary>支持浏览器登录的企业 provider。</summary>
public interface IEnterpriseLoginProvider : IEnterpriseProvider
{
    Task<Uri> CreateAuthorizationUriAsync(
        EnterpriseConnectionContext connection,
        EnterpriseLoginChallenge challenge,
        CancellationToken cancellationToken = default);

    Task<EnterpriseLoginIdentity?> RedeemAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        EnterpriseLoginCallback callback,
        CancellationToken cancellationToken = default);
}

/// <summary>目录同步返回的稳定外部用户。</summary>
public sealed record EnterpriseDirectoryUser(
    string ExternalId,
    string UserName,
    string? Email,
    string? DisplayName,
    bool Active,
    IReadOnlyList<string> GroupExternalIds);

/// <summary>目录同步返回的稳定外部部门或群组。</summary>
public sealed record EnterpriseDirectoryGroup(
    string ExternalId,
    string DisplayName);

/// <summary>有界目录同步页。</summary>
public sealed record EnterpriseDirectoryPage(
    IReadOnlyList<EnterpriseDirectoryUser> Users,
    IReadOnlyList<EnterpriseDirectoryGroup> Groups,
    string? NextCursor);

/// <summary>支持主动拉取用户和部门的企业 provider。</summary>
public interface IEnterpriseDirectoryProvider : IEnterpriseProvider
{
    Task<EnterpriseDirectoryPage> GetDirectoryPageAsync(
        EnterpriseConnectionContext connection,
        EnterpriseSecret secret,
        string? cursor,
        CancellationToken cancellationToken = default);
}

/// <summary>飞书、钉钉等 provider webhook 的幂等收据边界。</summary>
public interface IEnterpriseEventReceiptService
{
    Task<bool> TryRecordAsync(
        long connectionId,
        string eventId,
        string payloadHash,
        CancellationToken cancellationToken = default);
}

/// <summary>稳定外部身份到 GitCandy Identity 用户的绑定与 JIT 边界。</summary>
public interface IEnterpriseSignInService
{
    Task<EnterpriseSignInResult> ResolveAsync(
        EnterpriseConnectionContext connection,
        EnterpriseLoginIdentity identity,
        CancellationToken cancellationToken = default);
}

/// <summary>团队企业连接管理和运行时读取边界。</summary>
public interface IEnterpriseConnectionService
{
    Task<IReadOnlyList<EnterpriseConnectionSummary>?> GetForTeamAsync(
        string teamName,
        string? actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default);

    Task<EnterpriseConnectionSummary?> GetAsync(
        string teamName,
        long connectionId,
        string? actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default);

    Task<EnterpriseConnectionSummary?> SaveAsync(
        string teamName,
        EnterpriseConnectionEdit edit,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string teamName,
        long connectionId,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default);

    Task<EnterpriseProviderDiagnostic?> TestAsync(
        string teamName,
        long connectionId,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default);

    Task<EnterpriseConnectionContext?> GetRuntimeContextAsync(
        long connectionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EnterpriseLoginOption>> GetLoginOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EnterpriseConnectionContext>> GetProvisioningContextsAsync(
        CancellationToken cancellationToken = default);

    Task UpdateSyncStateAsync(
        long connectionId,
        string? cursor,
        EnterpriseConnectionStatus status,
        string? errorCode,
        bool completed,
        CancellationToken cancellationToken = default);
}
