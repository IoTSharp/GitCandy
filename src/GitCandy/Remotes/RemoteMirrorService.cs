using System.Collections.Concurrent;
using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Permissions;
using GitCandy.Git;
using GitCandy.Remotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GitCandy.Web.Remotes;

/// <summary>协调 EF mirror 状态、受控远程 Git backend 与本地 ref 应用。</summary>
public sealed class RemoteMirrorService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    IRemoteProviderCatalog providerCatalog,
    IRemoteCredentialVault credentialVault,
    IRemoteRepositorySyncBackend syncBackend,
    IRemoteMirrorReferenceStore referenceStore,
    IRemoteMirrorPushEventSink pushEventSink,
    IGitRepositoryPathResolver pathResolver,
    TimeProvider timeProvider,
    ILogger<RemoteMirrorService> logger) : IRemoteMirrorService, IDisposable
{
    private const int MaxBatchSize = 100;
    private const string HeadPrefix = "refs/heads/";
    private const string TagPrefix = "refs/tags/";
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly IRemoteProviderCatalog _providerCatalog = providerCatalog;
    private readonly IRemoteCredentialVault _credentialVault = credentialVault;
    private readonly IRemoteRepositorySyncBackend _syncBackend = syncBackend;
    private readonly IRemoteMirrorReferenceStore _referenceStore = referenceStore;
    private readonly IRemoteMirrorPushEventSink _pushEventSink = pushEventSink;
    private readonly IGitRepositoryPathResolver _pathResolver = pathResolver;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<RemoteMirrorService> _logger = logger;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _mirrorGates = new();

    public async Task<RemoteMirrorOperationResult> RegisterAsync(
        string actorUserId,
        RemoteMirrorRegistration registration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentNullException.ThrowIfNull(registration);
        if (!ValidateRegistration(registration))
        {
            return Failure(null, RemoteMirrorErrorCodes.InvalidConfiguration);
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await dbContext.RemoteAccountConnections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == registration.ConnectionId, cancellationToken);
        if (connection is null
            || !connection.IsEnabled
            || !await CanManageRepositoryAsync(dbContext, registration.RepositoryId, actorUserId, cancellationToken)
            || !await CanUseConnectionAsync(dbContext, connection, actorUserId, cancellationToken))
        {
            return Failure(null, RemoteMirrorErrorCodes.AccessDenied);
        }

        var protectedPatterns = await dbContext.BranchProtectionRules.AsNoTracking()
            .Where(item => item.RepositoryId == registration.RepositoryId)
            .Select(item => item.Pattern)
            .ToArrayAsync(cancellationToken);
        if (!RemoteMirrorRefFilter.TryCreate(
                registration.RefFilterKind,
                registration.RefFilterPattern,
                protectedPatterns,
                out _)
            || !MatchesConnection(connection, registration.RemoteRepository.Identity)
            || !TryCreateRemoteGitUrl(connection, registration.RemoteRepository, out var remoteGitUrl))
        {
            return Failure(null, RemoteMirrorErrorCodes.InvalidConfiguration);
        }

        var now = _timeProvider.GetUtcNow();
        var mirror = new GitCandyRepositoryMirror
        {
            RepositoryId = registration.RepositoryId,
            ConnectionId = registration.ConnectionId,
            RemoteRepositoryId = registration.RemoteRepository.Identity.ExternalId,
            RemoteOwnerLogin = registration.RemoteRepository.OwnerLogin,
            RemoteRepositoryName = registration.RemoteRepository.Name,
            RemoteGitUrl = remoteGitUrl.AbsoluteUri,
            Direction = registration.Direction,
            Authority = registration.Direction == RemoteMirrorDirection.Pull
                ? RemoteMirrorAuthority.Remote
                : RemoteMirrorAuthority.GitCandy,
            RefFilterKind = registration.RefFilterKind,
            RefFilterPattern = NormalizePattern(registration.RefFilterPattern),
            ScheduleIntervalMinutes = registration.ScheduleIntervalMinutes,
            ScheduleTimeZone = NormalizePattern(registration.ScheduleTimeZone),
            ScheduleEnabled = registration.ScheduleEnabled,
            DivergencePolicy = registration.DivergencePolicy,
            Prune = registration.PropagateDeletes,
            IsEnabled = registration.IsEnabled,
            Status = registration.IsEnabled ? RemoteMirrorStatus.Pending : RemoteMirrorStatus.Paused,
            CreatedAtUtc = now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime
        };
        dbContext.RepositoryMirrors.Add(mirror);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Failure(null, RemoteMirrorErrorCodes.InvalidConfiguration);
        }

        AddAudit(
            dbContext,
            mirror.RepositoryId,
            actorUserId,
            "mirror.register",
            "success",
            string.Empty,
            $"mirror={mirror.Id};direction={mirror.Direction}",
            now);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (!mirror.IsEnabled)
        {
            return new RemoteMirrorOperationResult(mirror.Id, true, RemoteMirrorStatus.Paused);
        }

        if (mirror.Direction == RemoteMirrorDirection.Push)
        {
            var repository = await CreateRepositoryContextAsync(mirror.RepositoryId, cancellationToken);
            if (repository is null)
            {
                return await RecordFailureAsync(mirror.Id, RemoteMirrorErrorCodes.NotFound, cancellationToken);
            }

            var localReferences = ReadPublicReferences(repository);
            if (localReferences.Count > 0)
            {
                var zeroObjectId = new string('0', localReferences.Values.First().Length);
                var events = localReferences
                    .Select(item => new RemoteMirrorRefEvent(zeroObjectId, item.Value, item.Key))
                    .ToArray();
                await _pushEventSink.EnqueueAsync(mirror.RepositoryId, events, cancellationToken);
            }
        }

        return await SynchronizeAsync(mirror.Id, cancellationToken);
    }

    public async Task<RemoteMirrorOperationResult> SynchronizeAsync(
        long mirrorId,
        CancellationToken cancellationToken = default)
    {
        if (mirrorId <= 0)
        {
            return Failure(null, RemoteMirrorErrorCodes.NotFound);
        }

        var gate = _mirrorGates.GetOrAdd(mirrorId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var mirror = await LoadMirrorAsync(mirrorId, cancellationToken);
            if (mirror is null)
            {
                return Failure(mirrorId, RemoteMirrorErrorCodes.NotFound);
            }
            if (!mirror.IsEnabled || mirror.Status == RemoteMirrorStatus.Paused || !mirror.ConnectionIsEnabled)
            {
                return Failure(mirrorId, RemoteMirrorErrorCodes.Disabled, RemoteMirrorStatus.Paused);
            }

            if (!RemoteMirrorRefFilter.TryCreate(
                    mirror.RefFilterKind,
                    mirror.RefFilterPattern,
                    mirror.ProtectedBranchPatterns,
                    out var filter))
            {
                return await RecordFailureAsync(mirrorId, RemoteMirrorErrorCodes.InvalidConfiguration, cancellationToken);
            }

            var provider = _providerCatalog.Get(mirror.Provider);
            var requiredCapability = mirror.Direction == RemoteMirrorDirection.Pull
                ? RemoteProviderCapabilities.PullMirror
                : RemoteProviderCapabilities.PushMirror;
            if (provider is null
                || (provider.Capabilities & requiredCapability) != requiredCapability
                || !UrisEqual(provider.ServerUrl, mirror.ProviderServerUrl))
            {
                return await RecordFailureAsync(mirrorId, RemoteMirrorErrorCodes.ProviderUnavailable, cancellationToken);
            }

            RemoteCredential? credential;
            try
            {
                credential = await _credentialVault.ResolveAsync(
                    new RemoteSecretReference(mirror.CredentialReference),
                    cancellationToken);
            }
            catch (ArgumentException)
            {
                credential = null;
            }
            if (credential is null)
            {
                return await RecordFailureAsync(mirrorId, RemoteMirrorErrorCodes.CredentialUnavailable, cancellationToken);
            }

            var operation = mirror.Direction == RemoteMirrorDirection.Pull
                ? RemoteRepositoryOperations.Pull
                : RemoteRepositoryOperations.Push;
            var scope = RemoteScopePolicy.Validate(
                credential.GrantedScopes,
                provider.GetRequiredScopes(mirror.AccountKind, operation));
            if (!scope.Satisfied)
            {
                return await RecordFailureAsync(mirrorId, RemoteMirrorErrorCodes.ScopeMissing, cancellationToken);
            }

            await MarkSynchronizingAsync(mirrorId, cancellationToken);
            var repository = new GitRepositoryContext(
                mirror.RepositoryStorageName,
                _pathResolver.ResolveRepositoryPath(mirror.RepositoryStorageName));
            var stagePrefix = RemoteMirrorReferenceNamespace.CreatePrefix(mirrorId);
            try
            {
                await FetchRemoteStageAsync(mirror, repository, stagePrefix, credential, cancellationToken);
                return mirror.Direction == RemoteMirrorDirection.Pull
                    ? await ApplyPullAsync(mirror, repository, stagePrefix, filter!, cancellationToken)
                    : await ApplyPushAsync(mirror, repository, stagePrefix, filter!, credential, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _ = await RecordFailureAsync(mirrorId, RemoteMirrorErrorCodes.Canceled, CancellationToken.None);
                throw;
            }
            catch (RemoteRepositorySyncException exception)
            {
                var code = exception.Code == RemoteRepositorySyncErrorCodes.NonFastForward
                    ? RemoteMirrorErrorCodes.Diverged
                    : exception.Code;
                _logger.LogWarning(
                    "Mirror {MirrorId} {Direction} failed with {ErrorCode}.",
                    mirrorId,
                    mirror.Direction,
                    code);
                return await RecordFailureAsync(mirrorId, code, cancellationToken);
            }
            catch (Exception exception) when (exception is ArgumentException
                or InvalidOperationException
                or LibGit2Sharp.LibGit2SharpException
                or IOException
                or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    exception,
                    "Mirror {MirrorId} has an invalid repository or ref configuration.",
                    mirrorId);
                return await RecordFailureAsync(
                    mirrorId,
                    RemoteMirrorErrorCodes.InvalidConfiguration,
                    cancellationToken);
            }
            finally
            {
                try
                {
                    _referenceStore.DeleteNamespace(repository, stagePrefix, CancellationToken.None);
                }
                catch (Exception exception) when (exception is InvalidOperationException
                    or LibGit2Sharp.LibGit2SharpException
                    or IOException
                    or UnauthorizedAccessException)
                {
                    _logger.LogWarning(exception, "Mirror {MirrorId} staging refs could not be cleaned.", mirrorId);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<RemoteMirrorOperationResult>> SynchronizeDuePullMirrorsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, MaxBatchSize);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await dbContext.RepositoryMirrors.AsNoTracking()
            .Where(item => item.Direction == RemoteMirrorDirection.Pull
                && item.IsEnabled
                && item.ScheduleEnabled
                && item.ScheduleIntervalMinutes != null
                && item.Status != RemoteMirrorStatus.Paused)
            .OrderBy(item => item.LastAttemptedAtUtc)
            .Select(item => new { item.Id, item.LastAttemptedAtUtc, item.ScheduleIntervalMinutes })
            .Take(limit * 4)
            .ToArrayAsync(cancellationToken);
        var dueIds = candidates
            .Where(item => item.LastAttemptedAtUtc is null
                || item.LastAttemptedAtUtc.Value.AddMinutes(item.ScheduleIntervalMinutes!.Value) <= now)
            .Take(limit)
            .Select(item => item.Id)
            .ToArray();
        return await SynchronizeManyAsync(dueIds, cancellationToken);
    }

    public async Task<IReadOnlyList<RemoteMirrorOperationResult>> ProcessPendingPushMirrorsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, MaxBatchSize);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var mirrorIds = await dbContext.RemoteMirrorRefUpdates.AsNoTracking()
            .Where(item => item.Mirror != null
                && item.Mirror.IsEnabled
                && item.Mirror.Direction == RemoteMirrorDirection.Push
                && item.Mirror.Status != RemoteMirrorStatus.Paused)
            .OrderBy(item => item.UpdatedAtUtc)
            .Select(item => item.MirrorId)
            .Distinct()
            .Take(limit)
            .ToArrayAsync(cancellationToken);
        return await SynchronizeManyAsync(mirrorIds, cancellationToken);
    }

    public async Task<bool> UpdateRemoteProfileAsync(
        long mirrorId,
        RemoteRepositoryProfile remoteRepository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteRepository);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var mirror = await dbContext.RepositoryMirrors
            .Include(item => item.Connection)
            .SingleOrDefaultAsync(item => item.Id == mirrorId, cancellationToken);
        if (mirror?.Connection is null
            || !string.Equals(
                mirror.RemoteRepositoryId,
                remoteRepository.Identity.ExternalId,
                StringComparison.Ordinal)
            || !MatchesConnection(mirror.Connection, remoteRepository.Identity)
            || !TryCreateRemoteGitUrl(mirror.Connection, remoteRepository, out var remoteGitUrl))
        {
            return false;
        }

        mirror.RemoteOwnerLogin = remoteRepository.OwnerLogin;
        mirror.RemoteRepositoryName = remoteRepository.Name;
        mirror.RemoteGitUrl = remoteGitUrl.AbsoluteUri;
        mirror.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public void Dispose()
    {
        foreach (var gate in _mirrorGates.Values)
        {
            gate.Dispose();
        }
    }

    private async Task FetchRemoteStageAsync(
        MirrorSnapshot mirror,
        GitRepositoryContext repository,
        string stagePrefix,
        RemoteCredential credential,
        CancellationToken cancellationToken)
    {
        var refSpecs = new RemoteRepositorySyncRefSpec[]
        {
            new(HeadPrefix + "*", stagePrefix + "heads/*", force: true),
            new(TagPrefix + "*", stagePrefix + "tags/*", force: true)
        };
        var request = new RemoteRepositorySyncRequest(
            repository,
            mirror.Provider,
            mirror.ProviderServerUrl,
            mirror.RemoteGitUrl,
            RemoteRepositorySyncOperation.Fetch,
            refSpecs,
            prune: true);
        await _syncBackend.ExecuteAsync(request, credential, cancellationToken);
    }

    private async Task<RemoteMirrorOperationResult> ApplyPullAsync(
        MirrorSnapshot mirror,
        GitRepositoryContext repository,
        string stagePrefix,
        RemoteMirrorRefFilter filter,
        CancellationToken cancellationToken)
    {
        var remote = ReadStagedReferences(repository, stagePrefix)
            .Where(item => filter.Matches(item.Key))
            .ToDictionary(StringComparer.Ordinal);
        var local = ReadPublicReferences(repository)
            .Where(item => filter.Matches(item.Key))
            .ToDictionary(StringComparer.Ordinal);
        var updates = new List<RemoteMirrorReferenceUpdate>();
        var divergent = new List<string>();
        var forced = new List<string>();

        foreach (var reference in remote)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!local.TryGetValue(reference.Key, out var localTarget))
            {
                updates.Add(new RemoteMirrorReferenceUpdate(reference.Key, reference.Value));
            }
            else if (!string.Equals(localTarget, reference.Value, StringComparison.OrdinalIgnoreCase)
                && !_referenceStore.IsAncestor(repository, localTarget, reference.Value, cancellationToken))
            {
                divergent.Add(reference.Key);
                if (mirror.DivergencePolicy == RemoteMirrorDivergencePolicy.OverwriteTarget)
                {
                    updates.Add(new RemoteMirrorReferenceUpdate(reference.Key, reference.Value));
                    forced.Add(reference.Key);
                }
            }
            else if (!string.Equals(localTarget, reference.Value, StringComparison.OrdinalIgnoreCase))
            {
                updates.Add(new RemoteMirrorReferenceUpdate(reference.Key, reference.Value));
            }
        }

        if (mirror.Prune)
        {
            updates.AddRange(local.Keys
                .Where(reference => !remote.ContainsKey(reference))
                .Select(reference => new RemoteMirrorReferenceUpdate(reference, null)));
        }
        if (divergent.Count > 0 && mirror.DivergencePolicy == RemoteMirrorDivergencePolicy.Stop)
        {
            return await CompleteAsync(
                mirror,
                RemoteMirrorStatus.Diverged,
                RemoteMirrorErrorCodes.Diverged,
                0,
                divergent.Count,
                observedRemoteHead: SelectObservedHead(remote),
                consumed: null,
                forcedReferences: [],
                cancellationToken);
        }

        _referenceStore.ApplyUpdates(repository, updates, cancellationToken);
        var unresolvedDivergence = divergent.Count > 0
            && mirror.DivergencePolicy != RemoteMirrorDivergencePolicy.OverwriteTarget;
        var status = unresolvedDivergence ? RemoteMirrorStatus.Diverged : RemoteMirrorStatus.Succeeded;
        return await CompleteAsync(
            mirror,
            status,
            unresolvedDivergence ? RemoteMirrorErrorCodes.Diverged : null,
            updates.Count,
            unresolvedDivergence ? divergent.Count : 0,
            SelectObservedHead(remote),
            consumed: null,
            forced,
            cancellationToken);
    }

    private async Task<RemoteMirrorOperationResult> ApplyPushAsync(
        MirrorSnapshot mirror,
        GitRepositoryContext repository,
        string stagePrefix,
        RemoteMirrorRefFilter filter,
        RemoteCredential credential,
        CancellationToken cancellationToken)
    {
        var remote = ReadStagedReferences(repository, stagePrefix);
        var local = ReadPublicReferences(repository);
        var pushSpecs = new List<RemoteRepositorySyncRefSpec>();
        var divergent = new List<string>();
        var forced = new List<string>();
        var consumed = new List<PendingRefSnapshot>();
        var pushCandidates = new List<PendingRefSnapshot>();

        foreach (var pending in mirror.PendingReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!filter.Matches(pending.ReferenceName))
            {
                consumed.Add(pending);
                continue;
            }

            local.TryGetValue(pending.ReferenceName, out var localTarget);
            remote.TryGetValue(pending.ReferenceName, out var remoteTarget);
            if (localTarget is null)
            {
                if (mirror.Prune && remoteTarget is not null)
                {
                    pushSpecs.Add(RemoteRepositorySyncRefSpec.Delete(pending.ReferenceName));
                    pushCandidates.Add(pending);
                }
                else
                {
                    consumed.Add(pending);
                }
                continue;
            }
            if (remoteTarget is null)
            {
                pushSpecs.Add(new RemoteRepositorySyncRefSpec(
                    pending.ReferenceName,
                    pending.ReferenceName));
                pushCandidates.Add(pending);
                continue;
            }
            if (string.Equals(localTarget, remoteTarget, StringComparison.OrdinalIgnoreCase))
            {
                consumed.Add(pending);
                continue;
            }
            if (_referenceStore.IsAncestor(repository, remoteTarget, localTarget, cancellationToken))
            {
                pushSpecs.Add(new RemoteRepositorySyncRefSpec(
                    pending.ReferenceName,
                    pending.ReferenceName));
                pushCandidates.Add(pending);
                continue;
            }

            divergent.Add(pending.ReferenceName);
            if (mirror.DivergencePolicy == RemoteMirrorDivergencePolicy.OverwriteTarget)
            {
                pushSpecs.Add(new RemoteRepositorySyncRefSpec(
                    pending.ReferenceName,
                    pending.ReferenceName,
                    force: true));
                pushCandidates.Add(pending);
                forced.Add(pending.ReferenceName);
            }
            else if (mirror.DivergencePolicy == RemoteMirrorDivergencePolicy.KeepDivergent)
            {
                consumed.Add(pending);
            }
        }

        if (divergent.Count > 0 && mirror.DivergencePolicy == RemoteMirrorDivergencePolicy.Stop)
        {
            return await CompleteAsync(
                mirror,
                RemoteMirrorStatus.Diverged,
                RemoteMirrorErrorCodes.Diverged,
                0,
                divergent.Count,
                SelectObservedHead(remote),
                consumed,
                forcedReferences: [],
                cancellationToken);
        }

        if (pushSpecs.Count > 0)
        {
            var request = new RemoteRepositorySyncRequest(
                repository,
                mirror.Provider,
                mirror.ProviderServerUrl,
                mirror.RemoteGitUrl,
                RemoteRepositorySyncOperation.Push,
                pushSpecs);
            await _syncBackend.ExecuteAsync(request, credential, cancellationToken);
            consumed.AddRange(pushCandidates);
        }

        var observedRemote = remote.ToDictionary(StringComparer.Ordinal);
        foreach (var refSpec in pushSpecs)
        {
            if (refSpec.IsDelete)
            {
                observedRemote.Remove(refSpec.DestinationReference);
            }
            else if (refSpec.SourceReference is string sourceReference
                && local.TryGetValue(sourceReference, out var target))
            {
                observedRemote[refSpec.DestinationReference] = target;
            }
        }

        var unresolvedDivergence = divergent.Count > 0
            && mirror.DivergencePolicy != RemoteMirrorDivergencePolicy.OverwriteTarget;
        var status = unresolvedDivergence ? RemoteMirrorStatus.Diverged : RemoteMirrorStatus.Succeeded;
        return await CompleteAsync(
            mirror,
            status,
            unresolvedDivergence ? RemoteMirrorErrorCodes.Diverged : null,
            pushSpecs.Count,
            unresolvedDivergence ? divergent.Count : 0,
            SelectObservedHead(observedRemote),
            consumed,
            forced,
            cancellationToken);
    }

    private async Task<RemoteMirrorOperationResult> CompleteAsync(
        MirrorSnapshot mirror,
        RemoteMirrorStatus status,
        string? errorCode,
        int updatedCount,
        int skippedCount,
        string? observedRemoteHead,
        IReadOnlyList<PendingRefSnapshot>? consumed,
        IReadOnlyList<string> forcedReferences,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RepositoryMirrors
            .SingleOrDefaultAsync(item => item.Id == mirror.Id, cancellationToken);
        if (entity is null)
        {
            return Failure(mirror.Id, RemoteMirrorErrorCodes.NotFound);
        }

        if (consumed is not null && consumed.Count > 0)
        {
            var names = consumed.Select(item => item.ReferenceName).Distinct(StringComparer.Ordinal).ToArray();
            var generations = consumed.ToDictionary(
                item => item.ReferenceName,
                item => item.Generation,
                StringComparer.Ordinal);
            var rows = await dbContext.RemoteMirrorRefUpdates
                .Where(item => item.MirrorId == mirror.Id && names.Contains(item.ReferenceName))
                .ToArrayAsync(cancellationToken);
            dbContext.RemoteMirrorRefUpdates.RemoveRange(rows.Where(item =>
                generations.TryGetValue(item.ReferenceName, out var generation)
                && generation == item.Generation));
        }

        entity.Status = status;
        entity.LastErrorCode = errorCode;
        entity.LastObservedRemoteHead = observedRemoteHead;
        entity.LastSucceededAtUtc = status == RemoteMirrorStatus.Succeeded
            ? now.UtcDateTime
            : entity.LastSucceededAtUtc;
        entity.UpdatedAtUtc = now.UtcDateTime;
        foreach (var reference in forcedReferences)
        {
            AddAudit(
                dbContext,
                mirror.RepositoryId,
                null,
                mirror.Direction == RemoteMirrorDirection.Pull ? "mirror.pull.force" : "mirror.push.force",
                "success",
                reference,
                $"mirror={mirror.Id}",
                now);
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        if (mirror.Direction == RemoteMirrorDirection.Push
            && status == RemoteMirrorStatus.Succeeded
            && await dbContext.RemoteMirrorRefUpdates.AsNoTracking()
                .AnyAsync(item => item.MirrorId == mirror.Id, cancellationToken))
        {
            entity.Status = RemoteMirrorStatus.Pending;
            entity.UpdatedAtUtc = now.UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
            status = RemoteMirrorStatus.Pending;
        }

        return new RemoteMirrorOperationResult(
            mirror.Id,
            status is RemoteMirrorStatus.Succeeded or RemoteMirrorStatus.Pending,
            status,
            errorCode,
            updatedCount,
            skippedCount);
    }

    private async Task MarkSynchronizingAsync(long mirrorId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RepositoryMirrors.SingleAsync(item => item.Id == mirrorId, cancellationToken);
        entity.Status = RemoteMirrorStatus.Synchronizing;
        entity.LastAttemptedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        entity.UpdatedAtUtc = entity.LastAttemptedAtUtc.Value;
        entity.LastErrorCode = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<RemoteMirrorOperationResult> RecordFailureAsync(
        long mirrorId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RepositoryMirrors.SingleOrDefaultAsync(
            item => item.Id == mirrorId,
            cancellationToken);
        if (entity is null)
        {
            return Failure(mirrorId, RemoteMirrorErrorCodes.NotFound);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        entity.Status = errorCode == RemoteMirrorErrorCodes.Diverged
            ? RemoteMirrorStatus.Diverged
            : errorCode == RemoteMirrorErrorCodes.Canceled
                ? RemoteMirrorStatus.Pending
                : RemoteMirrorStatus.Failed;
        entity.LastErrorCode = errorCode;
        entity.LastAttemptedAtUtc = now;
        entity.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Failure(mirrorId, errorCode, entity.Status);
    }

    private async Task<MirrorSnapshot?> LoadMirrorAsync(long mirrorId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RepositoryMirrors.AsNoTracking()
            .Include(item => item.Repository)
            .Include(item => item.Connection)
            .Include(item => item.PendingRefUpdates)
            .SingleOrDefaultAsync(item => item.Id == mirrorId, cancellationToken);
        if (entity?.Repository is null || entity.Connection is null)
        {
            return null;
        }

        var patterns = await dbContext.BranchProtectionRules.AsNoTracking()
            .Where(item => item.RepositoryId == entity.RepositoryId)
            .Select(item => item.Pattern)
            .ToArrayAsync(cancellationToken);
        if (!Uri.TryCreate(entity.Connection.ServerUrl, UriKind.Absolute, out var providerServerUrl)
            || !Uri.TryCreate(entity.RemoteGitUrl, UriKind.Absolute, out var remoteGitUrl))
        {
            return null;
        }

        return new MirrorSnapshot(
            entity.Id,
            entity.RepositoryId,
            entity.Repository.StorageName,
            entity.Direction,
            entity.RefFilterKind,
            entity.RefFilterPattern,
            entity.DivergencePolicy,
            entity.Prune,
            entity.IsEnabled,
            entity.Status,
            entity.Connection.IsEnabled
                && entity.Connection.Status is not (RemoteConnectionStatus.Revoked or RemoteConnectionStatus.Disabled),
            entity.Connection.Provider,
            providerServerUrl,
            remoteGitUrl,
            entity.Connection.AccountKind,
            entity.Connection.CredentialReference,
            patterns,
            entity.PendingRefUpdates
                .OrderBy(item => item.UpdatedAtUtc)
                .ThenBy(item => item.ReferenceName, StringComparer.Ordinal)
                .Take(1024)
                .Select(item => new PendingRefSnapshot(
                    item.ReferenceName,
                    item.OldObjectId,
                    item.NewObjectId,
                    item.Generation))
                .ToArray());
    }

    private IReadOnlyDictionary<string, string> ReadPublicReferences(GitRepositoryContext repository)
    {
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in _referenceStore.ReadReferences(repository, HeadPrefix))
        {
            references[item.Key] = item.Value;
        }
        foreach (var item in _referenceStore.ReadReferences(repository, TagPrefix))
        {
            references[item.Key] = item.Value;
        }
        return references;
    }

    private IReadOnlyDictionary<string, string> ReadStagedReferences(
        GitRepositoryContext repository,
        string stagePrefix)
    {
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in _referenceStore.ReadReferences(repository, stagePrefix))
        {
            var canonicalName = item.Key.StartsWith(stagePrefix + "heads/", StringComparison.Ordinal)
                ? HeadPrefix + item.Key[(stagePrefix + "heads/").Length..]
                : item.Key.StartsWith(stagePrefix + "tags/", StringComparison.Ordinal)
                    ? TagPrefix + item.Key[(stagePrefix + "tags/").Length..]
                    : null;
            if (canonicalName is not null)
            {
                references[canonicalName] = item.Value;
            }
        }
        return references;
    }

    private async Task<IReadOnlyList<RemoteMirrorOperationResult>> SynchronizeManyAsync(
        IEnumerable<long> mirrorIds,
        CancellationToken cancellationToken)
    {
        var results = new List<RemoteMirrorOperationResult>();
        foreach (var mirrorId in mirrorIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await SynchronizeAsync(mirrorId, cancellationToken));
        }
        return results;
    }

    private async Task<GitRepositoryContext?> CreateRepositoryContextAsync(
        long repositoryId,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var storageName = await dbContext.Repositories.AsNoTracking()
            .Where(item => item.Id == repositoryId)
            .Select(item => item.StorageName)
            .SingleOrDefaultAsync(cancellationToken);
        return storageName is null
            ? null
            : new GitRepositoryContext(storageName, _pathResolver.ResolveRepositoryPath(storageName));
    }

    private static bool ValidateRegistration(RemoteMirrorRegistration registration)
    {
        if (registration.RepositoryId <= 0
            || registration.ConnectionId <= 0
            || !Enum.IsDefined(registration.Direction)
            || !Enum.IsDefined(registration.RefFilterKind)
            || !Enum.IsDefined(registration.DivergencePolicy)
            || registration.ScheduleIntervalMinutes is not null and (< 5 or > 10080)
            || (registration.ScheduleEnabled && registration.ScheduleIntervalMinutes is null)
            || (registration.ScheduleIntervalMinutes is null) != string.IsNullOrWhiteSpace(registration.ScheduleTimeZone))
        {
            return false;
        }

        if (registration.ScheduleTimeZone is not null)
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(registration.ScheduleTimeZone.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> CanManageRepositoryAsync(
        GitCandyDbContext dbContext,
        long repositoryId,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var administratorRole = RoleNames.Administrator.ToUpperInvariant();
        var isAdministrator = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == actorUserId && role.NormalizedName == administratorRole
            select userRole)
            .AnyAsync(cancellationToken);
        var permissions = new GitCandyRepositoryPermissionQuery(dbContext);
        return await permissions.IsRepositoryOwnerAsync(
            repositoryId,
            actorUserId,
            isAdministrator,
            cancellationToken);
    }

    private static async Task<bool> CanUseConnectionAsync(
        GitCandyDbContext dbContext,
        GitCandyRemoteAccountConnection connection,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (connection.OwnerKind == RemoteConnectionOwnerKind.User)
        {
            return string.Equals(connection.OwnerUserId, actorUserId, StringComparison.Ordinal);
        }
        if (connection.OwnerTeamId is not long teamId)
        {
            return false;
        }

        var role = await dbContext.UserTeamRoles.AsNoTracking()
            .Where(item => item.TeamId == teamId && item.UserId == actorUserId)
            .Select(item => (GitCandy.Teams.TeamRole?)item.Role)
            .SingleOrDefaultAsync(cancellationToken);
        return role is GitCandy.Teams.TeamRole.TeamOwner or GitCandy.Teams.TeamRole.Leader;
    }

    private static bool MatchesConnection(
        GitCandyRemoteAccountConnection connection,
        RemoteRepositoryIdentity identity)
    {
        return connection.Provider == identity.Provider
            && string.Equals(connection.ServerUrl.TrimEnd('/'), identity.ServerUrl.AbsoluteUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCreateRemoteGitUrl(
        GitCandyRemoteAccountConnection connection,
        RemoteRepositoryProfile repository,
        out Uri remoteGitUrl)
    {
        var builder = new UriBuilder(repository.WebUrl)
        {
            Path = repository.WebUrl.AbsolutePath.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? repository.WebUrl.AbsolutePath
                : repository.WebUrl.AbsolutePath.TrimEnd('/') + ".git",
            Query = string.Empty,
            Fragment = string.Empty
        };
        remoteGitUrl = builder.Uri;
        if (!Uri.TryCreate(connection.ServerUrl, UriKind.Absolute, out var providerServerUrl))
        {
            return false;
        }

        try
        {
            _ = new RemoteRepositorySyncRequest(
                new GitRepositoryContext("validation", Path.GetFullPath("validation")),
                connection.Provider,
                providerServerUrl,
                remoteGitUrl,
                RemoteRepositorySyncOperation.Fetch,
                [new RemoteRepositorySyncRefSpec("refs/heads/*", "refs/heads/*")]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool UrisEqual(Uri left, Uri right)
    {
        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
            && left.Port == right.Port
            && string.Equals(left.AbsolutePath.TrimEnd('/'), right.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);
    }

    private static string? SelectObservedHead(IReadOnlyDictionary<string, string> references)
    {
        if (references.TryGetValue("refs/heads/main", out var main))
        {
            return main;
        }
        if (references.TryGetValue("refs/heads/master", out var master))
        {
            return master;
        }
        return references
            .Where(item => item.Key.StartsWith(HeadPrefix, StringComparison.Ordinal))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Value)
            .FirstOrDefault();
    }

    private static string? NormalizePattern(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RemoteMirrorOperationResult Failure(
        long? mirrorId,
        string errorCode,
        RemoteMirrorStatus status = RemoteMirrorStatus.Failed) =>
        new(mirrorId, false, status, errorCode);

    private static void AddAudit(
        GitCandyDbContext dbContext,
        long repositoryId,
        string? actorUserId,
        string action,
        string outcome,
        string referenceName,
        string detail,
        DateTimeOffset occurredAt)
    {
        dbContext.GovernanceAuditEvents.Add(new GitCandyGovernanceAuditEvent
        {
            RepositoryId = repositoryId,
            ActorUserId = actorUserId,
            Action = action,
            Outcome = outcome,
            ReferenceName = referenceName,
            Detail = detail,
            OccurredAtUtc = occurredAt.UtcDateTime
        });
    }

    private sealed record MirrorSnapshot(
        long Id,
        long RepositoryId,
        string RepositoryStorageName,
        RemoteMirrorDirection Direction,
        RemoteMirrorRefFilterKind RefFilterKind,
        string? RefFilterPattern,
        RemoteMirrorDivergencePolicy DivergencePolicy,
        bool Prune,
        bool IsEnabled,
        RemoteMirrorStatus Status,
        bool ConnectionIsEnabled,
        RemoteProviderKind Provider,
        Uri ProviderServerUrl,
        Uri RemoteGitUrl,
        RemoteAccountKind AccountKind,
        string CredentialReference,
        IReadOnlyList<string> ProtectedBranchPatterns,
        IReadOnlyList<PendingRefSnapshot> PendingReferences);

    private sealed record PendingRefSnapshot(
        string ReferenceName,
        string OldObjectId,
        string NewObjectId,
        long Generation);
}
