using GitCandy.Enterprise;

namespace GitCandy.Web.Enterprise;

/// <summary>有界、可恢复并按连接隔离失败的企业目录同步器。</summary>
public sealed class EnterpriseDirectorySyncService(
    IEnterpriseConnectionService connectionService,
    IEnterpriseSecretResolver secretResolver,
    IEnumerable<IEnterpriseProvider> providers,
    IScimProvisioningService provisioningService,
    ILogger<EnterpriseDirectorySyncService> logger) : IEnterpriseDirectorySyncService
{
    private const int MaxPages = 200;
    private const int MaxUsers = 50_000;
    private readonly IEnterpriseConnectionService _connectionService = connectionService;
    private readonly IEnterpriseSecretResolver _secretResolver = secretResolver;
    private readonly IReadOnlyDictionary<EnterpriseProviderKind, IEnterpriseDirectoryProvider> _providers = providers
        .OfType<IEnterpriseDirectoryProvider>()
        .ToDictionary(provider => provider.Kind);
    private readonly IScimProvisioningService _provisioningService = provisioningService;
    private readonly ILogger<EnterpriseDirectorySyncService> _logger = logger;

    public async Task<IReadOnlyList<EnterpriseDirectorySyncResult>> SynchronizeAllAsync(
        CancellationToken cancellationToken = default)
    {
        var contexts = await _connectionService.GetProvisioningContextsAsync(cancellationToken);
        var results = new List<EnterpriseDirectorySyncResult>(contexts.Count);
        foreach (var connection in contexts.Where(item => _providers.ContainsKey(item.Provider)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(await SynchronizeAsync(connection.Id, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Enterprise directory connection {ConnectionId} failed without stopping other connections.",
                    connection.Id);
                results.Add(new EnterpriseDirectorySyncResult(
                    connection.Id,
                    false,
                    0,
                    0,
                    0,
                    "sync_failed"));
            }
        }

        return results;
    }

    public async Task<EnterpriseDirectorySyncResult> SynchronizeAsync(
        long connectionId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionService.GetRuntimeContextAsync(connectionId, cancellationToken);
        if (connection is null
            || !connection.IsEnabled
            || !connection.ProvisioningEnabled
            || !_providers.TryGetValue(connection.Provider, out var provider))
        {
            return new EnterpriseDirectorySyncResult(connectionId, false, 0, 0, 0, "provider_unavailable");
        }

        var secret = await _secretResolver.ResolveAsync(connection.SecretReference, cancellationToken);
        if (secret is null)
        {
            await _connectionService.UpdateSyncStateAsync(
                connectionId,
                connection.SyncCursor,
                EnterpriseConnectionStatus.Failed,
                "secret_unavailable",
                completed: false,
                cancellationToken);
            return new EnterpriseDirectorySyncResult(connectionId, false, 0, 0, 0, "secret_unavailable");
        }

        var startedFresh = string.IsNullOrWhiteSpace(connection.SyncCursor);
        var cursor = connection.SyncCursor;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var usersByExternalId = new Dictionary<string, long>(StringComparer.Ordinal);
        var groups = new Dictionary<string, string>(StringComparer.Ordinal);
        var groupMembers = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        var userCount = 0;
        try
        {
            for (var pageNumber = 0; pageNumber < MaxPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(cursor) && !seenCursors.Add(cursor))
                {
                    throw new InvalidOperationException("The provider returned a repeating directory cursor.");
                }

                var page = await provider.GetDirectoryPageAsync(
                    connection,
                    secret,
                    cursor,
                    cancellationToken);
                if (page.Users.Count + userCount > MaxUsers)
                {
                    throw new InvalidOperationException("The directory synchronization exceeded the user limit.");
                }

                foreach (var group in page.Groups)
                {
                    if (!string.IsNullOrWhiteSpace(group.ExternalId))
                    {
                        groups[group.ExternalId] = group.DisplayName;
                    }
                }

                foreach (var user in page.Users)
                {
                    var result = await _provisioningService.UpsertUserAsync(
                        connectionId,
                        new ScimUserData(
                            user.ExternalId,
                            user.UserName,
                            user.Email,
                            user.DisplayName,
                            user.Active),
                        cancellationToken);
                    if (!result.Succeeded || result.Resource is null)
                    {
                        throw new InvalidOperationException($"Directory user upsert failed with {result.ErrorCode}.");
                    }

                    usersByExternalId[user.ExternalId] = result.Resource.Id;
                    foreach (var groupId in user.GroupExternalIds)
                    {
                        if (!groupMembers.TryGetValue(groupId, out var members))
                        {
                            members = [];
                            groupMembers[groupId] = members;
                        }

                        members.Add(result.Resource.Id);
                    }
                }

                userCount += page.Users.Count;
                cursor = string.IsNullOrWhiteSpace(page.NextCursor) ? null : page.NextCursor;
                await _connectionService.UpdateSyncStateAsync(
                    connectionId,
                    cursor,
                    EnterpriseConnectionStatus.Healthy,
                    null,
                    completed: false,
                    cancellationToken);
                if (cursor is null)
                {
                    break;
                }

                if (pageNumber == MaxPages - 1)
                {
                    throw new InvalidOperationException("The directory synchronization exceeded the page limit.");
                }
            }

            var groupCount = 0;
            foreach (var group in startedFresh ? groups : [])
            {
                var result = await _provisioningService.UpsertGroupAsync(
                    connectionId,
                    new ScimGroupData(
                        group.Key,
                        group.Value,
                        groupMembers.GetValueOrDefault(group.Key)?.ToArray() ?? []),
                    cancellationToken);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Directory group upsert failed with {result.ErrorCode}.");
                }

                groupCount++;
            }

            var deprovision = startedFresh
                ? await _provisioningService.DeactivateMissingUsersAsync(
                    connectionId,
                    usersByExternalId.Keys.ToArray(),
                    cancellationToken)
                : new EnterpriseDeprovisionResult(0, 0, 0);
            var degraded = deprovision.ProtectedOwners > 0 || deprovision.Failed > 0;
            var errorCode = deprovision.ProtectedOwners > 0
                ? "protected_owner"
                : deprovision.Failed > 0 ? "partial_deprovision" : null;
            await _connectionService.UpdateSyncStateAsync(
                connectionId,
                null,
                degraded ? EnterpriseConnectionStatus.Degraded : EnterpriseConnectionStatus.Healthy,
                errorCode,
                completed: true,
                cancellationToken);
            return new EnterpriseDirectorySyncResult(
                connectionId,
                true,
                userCount,
                groupCount,
                deprovision.Deactivated,
                errorCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Enterprise directory connection {ConnectionId} synchronization failed.", connectionId);
            await _connectionService.UpdateSyncStateAsync(
                connectionId,
                cursor,
                EnterpriseConnectionStatus.Failed,
                "sync_failed",
                completed: false,
                cancellationToken);
            return new EnterpriseDirectorySyncResult(connectionId, false, userCount, 0, 0, "sync_failed");
        }
    }
}
