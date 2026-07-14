using System.Text.Json;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Remotes;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class RemoteConnectionService(
    GitCandyDbContext dbContext,
    IRemoteProviderCatalog providerCatalog,
    IRemoteCredentialVault credentialVault,
    TimeProvider timeProvider) : IRemoteConnectionService
{
    private const int MaximumSecretLength = 16 * 1024;
    private const int MaximumRepositoryPageSize = 100;
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly IRemoteProviderCatalog _providerCatalog = providerCatalog;
    private readonly IRemoteCredentialVault _credentialVault = credentialVault;
    private readonly TimeProvider _timeProvider = timeProvider;

    public IReadOnlyList<RemoteProviderDescriptor> AvailableProviders { get; } =
        providerCatalog.AvailableProviders
            .Select(kind => providerCatalog.Get(kind))
            .Where(static provider => provider is not null)
            .Cast<IRemoteRepositoryProvider>()
            .Select(static provider => new RemoteProviderDescriptor(
                provider.Kind,
                ProviderDisplayName(provider.Kind),
                provider.ServerUrl,
                provider.Capabilities,
                provider.AuthenticationKinds,
                provider.GetRequiredScopes(RemoteAccountKind.User, RemoteRepositoryOperations.Discover)))
            .ToArray();

    public async Task<IReadOnlyList<RemoteConnectionSummary>> GetForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var connections = await _dbContext.RemoteAccountConnections.AsNoTracking()
            .Where(item => item.OwnerKind == RemoteConnectionOwnerKind.User
                && item.OwnerUserId == userId)
            .OrderBy(item => item.Provider)
            .ThenBy(item => item.Login)
            .ToArrayAsync(cancellationToken);
        return connections.Select(ToSummary).ToArray();
    }

    public async Task<RemoteConnectionResult> ConnectUserAsync(
        string userId,
        RemoteUserConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(request);

        if (!await _dbContext.Users.AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken))
        {
            return FailedConnection("invalid_owner", "The current user account is unavailable.");
        }

        if (!Enum.IsDefined(request.Provider)
            || !Enum.IsDefined(request.AuthenticationKind)
            || request.Secret.Value.Length > MaximumSecretLength
            || !_providerCatalog.AvailableProviders.Contains(request.Provider))
        {
            return FailedConnection("invalid_connection", "The remote connection settings are invalid.");
        }

        var provider = _providerCatalog.Get(request.Provider);
        if (provider is null || !provider.AuthenticationKinds.Contains(request.AuthenticationKind))
        {
            return FailedConnection("provider_unavailable", "The selected remote provider or authentication method is unavailable.");
        }

        RemoteCredential credential;
        try
        {
            credential = new RemoteCredential(
                request.AuthenticationKind,
                request.Secret,
                request.GrantedScopes,
                request.ExpiresAt);
        }
        catch (ArgumentException)
        {
            return FailedConnection("invalid_scopes", "The granted scope list is invalid.");
        }

        var serializedScopes = SerializeScopes(credential.GrantedScopes);
        if (serializedScopes.Length > SchemaLimits.RemoteGrantedScopes)
        {
            return FailedConnection("invalid_scopes", "The granted scope list is too large.");
        }

        var requiredScopes = provider.GetRequiredScopes(
            RemoteAccountKind.User,
            RemoteRepositoryOperations.Discover);
        var scopeValidation = RemoteScopePolicy.Validate(credential.GrantedScopes, requiredScopes);
        if (!scopeValidation.Satisfied)
        {
            return FailedConnection(
                "missing_scopes",
                $"The credential is missing required scopes: {string.Join(", ", scopeValidation.MissingScopes)}.");
        }

        var provisionalConnection = CreateProvisionalContext(userId, provider, credential);
        var diagnostic = await TestProviderAsync(provider, provisionalConnection, credential, cancellationToken);
        if (!diagnostic.Succeeded)
        {
            return new RemoteConnectionResult(null, diagnostic);
        }

        RemoteAccountProfile? account;
        try
        {
            account = await provider.GetAccountAsync(provisionalConnection, credential, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RemoteProviderException exception)
        {
            return FailedConnection(exception.Code, exception.Message);
        }
        catch (Exception)
        {
            return FailedConnection("provider_error", "The provider account could not be read.");
        }

        if (account is null || !IsValidAccount(provider, account))
        {
            return FailedConnection("invalid_response", "The provider returned an invalid account identity.");
        }

        var serverUrl = account.Identity.ServerUrl.AbsoluteUri;
        var duplicate = await _dbContext.RemoteAccountConnections.AsNoTracking().AnyAsync(
            item => item.Provider == account.Identity.Provider
                && item.ServerUrl == serverUrl
                && item.ExternalAccountId == account.Identity.ExternalId,
            cancellationToken);
        if (duplicate)
        {
            return FailedConnection("already_connected", "This remote account is already connected.");
        }

        var owner = new RemoteConnectionOwner(RemoteConnectionOwnerKind.User, userId);
        RemoteCredentialMetadata metadata;
        try
        {
            metadata = await _credentialVault.StoreAsync(owner, credential, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return FailedConnection("credential_store_unavailable", "The remote credential could not be stored securely.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var entity = new GitCandyRemoteAccountConnection
        {
            OwnerKind = RemoteConnectionOwnerKind.User,
            OwnerUserId = userId,
            Provider = account.Identity.Provider,
            ServerUrl = serverUrl,
            ExternalAccountId = account.Identity.ExternalId,
            AccountKind = account.Kind,
            Login = account.Login.Trim(),
            DisplayName = NullIfWhiteSpace(account.DisplayName),
            AuthenticationKind = credential.AuthenticationKind,
            CredentialReference = metadata.Reference.Value,
            GrantedScopes = serializedScopes,
            IsEnabled = true,
            Status = RemoteConnectionStatus.Healthy,
            LastTestedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _dbContext.RemoteAccountConnections.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await _credentialVault.RevokeAsync(metadata.Reference, CancellationToken.None);
            return FailedConnection("already_connected", "This remote account could not be connected because its stable identity is already in use.");
        }

        AddAudit(
            entity.Id,
            userId,
            "remote.connection.create",
            "succeeded",
            $"Provider={entity.Provider}; account={entity.ExternalAccountId}.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new RemoteConnectionResult(
            ToSummary(entity),
            new RemoteProviderDiagnostic(true, "connected", "The remote account was connected."));
    }

    public async Task<RemoteProviderDiagnostic?> TestUserConnectionAsync(
        string userId,
        long connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var connection = await FindUserConnectionAsync(userId, connectionId, cancellationToken);
        if (connection is null)
        {
            return null;
        }

        var diagnostic = await TestConnectionAsync(connection, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        connection.LastTestedAtUtc = now;
        connection.UpdatedAtUtc = now;
        connection.LastErrorCode = diagnostic.Succeeded ? null : diagnostic.Code;
        connection.Status = diagnostic.Succeeded
            ? RemoteConnectionStatus.Healthy
            : diagnostic.Code is "credential_unavailable" or "credential_expired"
                ? RemoteConnectionStatus.Revoked
                : RemoteConnectionStatus.Failed;
        AddAudit(
            connection.Id,
            userId,
            "remote.connection.test",
            diagnostic.Succeeded ? "succeeded" : "failed",
            $"Provider={connection.Provider}; code={diagnostic.Code}.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return diagnostic;
    }

    public async Task<RemoteRepositoryDiscoveryResult?> DiscoverRepositoriesAsync(
        string userId,
        long connectionId,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var connection = await _dbContext.RemoteAccountConnections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == connectionId
                && item.OwnerKind == RemoteConnectionOwnerKind.User
                && item.OwnerUserId == userId,
                cancellationToken);
        if (connection is null)
        {
            return null;
        }

        if (!connection.IsEnabled)
        {
            return FailedDiscovery("connection_disabled", "The remote connection is disabled.");
        }

        var provider = _providerCatalog.Get(connection.Provider);
        if (provider is null)
        {
            return FailedDiscovery("provider_unavailable", "The remote provider is unavailable.");
        }

        var credential = await ResolveCredentialAsync(connection, cancellationToken);
        if (credential is null)
        {
            return FailedDiscovery("credential_unavailable", "The remote credential is unavailable or expired.");
        }

        try
        {
            var page = await provider.GetRepositoriesAsync(
                ToContext(connection),
                credential,
                cursor,
                cancellationToken);
            var boundedPage = new RemoteRepositoryPage(
                page.Repositories.Take(MaximumRepositoryPageSize).ToArray(),
                page.NextCursor);
            return new RemoteRepositoryDiscoveryResult(
                boundedPage,
                new RemoteProviderDiagnostic(true, "ok", "Remote repositories were loaded."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RemoteProviderException exception)
        {
            return FailedDiscovery(exception.Code, exception.Message);
        }
        catch (Exception)
        {
            return FailedDiscovery("provider_error", "The remote repositories could not be loaded.");
        }
    }

    public async Task<bool> DisconnectUserAsync(
        string userId,
        long connectionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var connection = await FindUserConnectionAsync(userId, connectionId, cancellationToken);
        if (connection is null || await _dbContext.RepositoryMirrors.AsNoTracking()
            .AnyAsync(item => item.ConnectionId == connectionId, cancellationToken))
        {
            return false;
        }

        connection.IsEnabled = false;
        connection.Status = RemoteConnectionStatus.Disabled;
        connection.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var reference = new RemoteSecretReference(connection.CredentialReference);
        await _credentialVault.RevokeAsync(reference, cancellationToken);
        AddAudit(
            connection.Id,
            userId,
            "remote.connection.revoke",
            "succeeded",
            $"Provider={connection.Provider}; account={connection.ExternalAccountId}.");
        _dbContext.RemoteAccountConnections.Remove(connection);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<RemoteProviderDiagnostic> TestConnectionAsync(
        GitCandyRemoteAccountConnection connection,
        CancellationToken cancellationToken)
    {
        if (!connection.IsEnabled)
        {
            return new RemoteProviderDiagnostic(false, "connection_disabled", "The remote connection is disabled.");
        }

        var provider = _providerCatalog.Get(connection.Provider);
        if (provider is null)
        {
            return new RemoteProviderDiagnostic(false, "provider_unavailable", "The remote provider is unavailable.");
        }

        var credential = await ResolveCredentialAsync(connection, cancellationToken);
        if (credential is null)
        {
            return new RemoteProviderDiagnostic(false, "credential_unavailable", "The remote credential is unavailable or expired.");
        }

        var context = ToContext(connection);
        var diagnostic = await TestProviderAsync(provider, context, credential, cancellationToken);
        if (!diagnostic.Succeeded)
        {
            return diagnostic;
        }

        try
        {
            var account = await provider.GetAccountAsync(context, credential, cancellationToken);
            if (account is null
                || !IsValidAccount(provider, account)
                || !string.Equals(account.Identity.ExternalId, connection.ExternalAccountId, StringComparison.Ordinal))
            {
                return new RemoteProviderDiagnostic(false, "identity_changed", "The remote credential no longer resolves to the connected account.");
            }

            connection.Login = account.Login.Trim();
            connection.DisplayName = NullIfWhiteSpace(account.DisplayName);
            return diagnostic;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RemoteProviderException exception)
        {
            return new RemoteProviderDiagnostic(false, exception.Code, exception.Message);
        }
        catch (Exception)
        {
            return new RemoteProviderDiagnostic(false, "provider_error", "The provider account could not be read.");
        }
    }

    private async Task<RemoteCredential?> ResolveCredentialAsync(
        GitCandyRemoteAccountConnection connection,
        CancellationToken cancellationToken)
    {
        RemoteCredential? credential;
        try
        {
            credential = await _credentialVault.ResolveAsync(
                new RemoteSecretReference(connection.CredentialReference),
                cancellationToken);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return credential?.ExpiresAt is DateTimeOffset expiresAt
            && expiresAt <= _timeProvider.GetUtcNow()
                ? null
                : credential;
    }

    private static async Task<RemoteProviderDiagnostic> TestProviderAsync(
        IRemoteRepositoryProvider provider,
        RemoteAccountConnectionContext context,
        RemoteCredential credential,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.TestAsync(context, credential, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RemoteProviderException exception)
        {
            return new RemoteProviderDiagnostic(false, exception.Code, exception.Message);
        }
        catch (Exception)
        {
            return new RemoteProviderDiagnostic(false, "provider_error", "The provider connection test failed.");
        }
    }

    private Task<GitCandyRemoteAccountConnection?> FindUserConnectionAsync(
        string userId,
        long connectionId,
        CancellationToken cancellationToken) => _dbContext.RemoteAccountConnections
        .SingleOrDefaultAsync(item => item.Id == connectionId
            && item.OwnerKind == RemoteConnectionOwnerKind.User
            && item.OwnerUserId == userId,
            cancellationToken);

    private void AddAudit(
        long connectionId,
        string actorUserId,
        string action,
        string outcome,
        string detail)
    {
        _dbContext.CredentialAuditEvents.Add(new GitCandyCredentialAuditEvent
        {
            CredentialKind = "remote-account",
            CredentialId = connectionId,
            ActorUserId = actorUserId,
            Action = action,
            Outcome = outcome,
            Detail = detail,
            OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        });
    }

    private static RemoteAccountConnectionContext CreateProvisionalContext(
        string userId,
        IRemoteRepositoryProvider provider,
        RemoteCredential credential) => new(
            0,
            new RemoteConnectionOwner(RemoteConnectionOwnerKind.User, userId),
            new RemoteAccountProfile(
                new RemoteAccountIdentity(provider.Kind, provider.ServerUrl.AbsoluteUri, "pending"),
                RemoteAccountKind.User,
                "pending",
                null),
            credential.AuthenticationKind,
            new RemoteSecretReference("pending:credential"),
            credential.GrantedScopes,
            true);

    private static RemoteAccountConnectionContext ToContext(
        GitCandyRemoteAccountConnection connection) => new(
            connection.Id,
            new RemoteConnectionOwner(
                connection.OwnerKind,
                connection.OwnerUserId ?? $"team-{connection.OwnerTeamId}"),
            new RemoteAccountProfile(
                new RemoteAccountIdentity(
                    connection.Provider,
                    connection.ServerUrl,
                    connection.ExternalAccountId),
                connection.AccountKind,
                connection.Login,
                connection.DisplayName),
            connection.AuthenticationKind,
            new RemoteSecretReference(connection.CredentialReference),
            DeserializeScopes(connection.GrantedScopes),
            connection.IsEnabled);

    private static bool IsValidAccount(
        IRemoteRepositoryProvider provider,
        RemoteAccountProfile account) =>
        account.Identity.Provider == provider.Kind
        && account.Identity.ServerUrl == provider.ServerUrl
        && !string.IsNullOrWhiteSpace(account.Identity.ExternalId)
        && account.Identity.ExternalId.Length <= SchemaLimits.RemoteExternalId
        && !string.IsNullOrWhiteSpace(account.Login)
        && account.Login.Trim().Length <= SchemaLimits.RemoteLogin
        && (string.IsNullOrWhiteSpace(account.DisplayName)
            || account.DisplayName.Trim().Length <= SchemaLimits.UserDisplayName);

    private static RemoteConnectionSummary ToSummary(GitCandyRemoteAccountConnection connection) => new(
        connection.Id,
        connection.Provider,
        new RemoteAccountIdentity(
            connection.Provider,
            connection.ServerUrl,
            connection.ExternalAccountId).ServerUrl,
        connection.ExternalAccountId,
        connection.AccountKind,
        connection.Login,
        connection.DisplayName,
        connection.AuthenticationKind,
        DeserializeScopes(connection.GrantedScopes),
        connection.IsEnabled,
        connection.Status,
        connection.LastErrorCode,
        ToDateTimeOffset(connection.LastTestedAtUtc),
        ToDateTimeOffset(connection.CreatedAtUtc)!.Value,
        ToDateTimeOffset(connection.UpdatedAtUtc)!.Value);

    private static string SerializeScopes(IReadOnlySet<string> scopes) =>
        JsonSerializer.Serialize(scopes.Order(StringComparer.Ordinal));

    private static IReadOnlySet<string> DeserializeScopes(string scopes)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(scopes) ?? [];
            if (values.Any(string.IsNullOrWhiteSpace))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            return new HashSet<string>(
                values.Select(static value => value.Trim()),
                StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static RemoteConnectionResult FailedConnection(string code, string message) => new(
        null,
        new RemoteProviderDiagnostic(false, code, message));

    private static RemoteRepositoryDiscoveryResult FailedDiscovery(string code, string message) => new(
        null,
        new RemoteProviderDiagnostic(false, code, message));

    private static string ProviderDisplayName(RemoteProviderKind kind) => kind switch
    {
        RemoteProviderKind.GitHub => "GitHub",
        RemoteProviderKind.GitLab => "GitLab",
        RemoteProviderKind.Gitee => "Gitee",
        _ => kind.ToString()
    };

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset? ToDateTimeOffset(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
