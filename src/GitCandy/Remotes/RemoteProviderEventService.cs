using System.Security.Cryptography;
using System.Text.Json;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Git;
using GitCandy.Remotes;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Web.Remotes;

/// <summary>持久化 provider event 收据，并用 stable repository ID 驱动 rename/delete 和 Pull 对账。</summary>
public sealed class RemoteProviderEventService(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    IRemoteMirrorService mirrorService,
    IRemoteMirrorJobQueue jobQueue,
    TimeProvider timeProvider) : IRemoteProviderEventService
{
    private const int MaxDeliveryIdLength = 128;
    private const int MaxEventTypeLength = 128;
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly IRemoteMirrorService _mirrorService = mirrorService;
    private readonly IRemoteMirrorJobQueue _jobQueue = jobQueue;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<string?> GetWebhookSecretReferenceAsync(
        long connectionId,
        RemoteProviderKind provider,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.RemoteAccountConnections.AsNoTracking()
            .Where(item => item.Id == connectionId
                && item.Provider == provider
                && item.IsEnabled)
            .Select(item => item.WebhookSecretReference)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<RemoteProviderEventResult> ProcessAsync(
        RemoteProviderEvent remoteEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEvent);
        if (remoteEvent.ConnectionId <= 0
            || string.IsNullOrWhiteSpace(remoteEvent.DeliveryId)
            || remoteEvent.DeliveryId.Length > MaxDeliveryIdLength
            || string.IsNullOrWhiteSpace(remoteEvent.EventType)
            || remoteEvent.EventType.Length > MaxEventTypeLength)
        {
            return new RemoteProviderEventResult(false, false, 0, "invalid_event");
        }

        ProviderRepositoryEvent repositoryEvent;
        try
        {
            repositoryEvent = Parse(remoteEvent.Provider, remoteEvent.EventType, remoteEvent.Payload.Span);
        }
        catch (JsonException)
        {
            return new RemoteProviderEventResult(false, false, 0, "invalid_payload");
        }
        catch (ArgumentException)
        {
            return new RemoteProviderEventResult(false, false, 0, "invalid_payload");
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await dbContext.RemoteAccountConnections
            .SingleOrDefaultAsync(item => item.Id == remoteEvent.ConnectionId
                && item.Provider == remoteEvent.Provider
                && item.IsEnabled,
                cancellationToken);
        if (connection is null)
        {
            return new RemoteProviderEventResult(false, false, 0, "connection_not_found");
        }
        if (await dbContext.RemoteProviderEvents.AsNoTracking().AnyAsync(
                item => item.ConnectionId == remoteEvent.ConnectionId
                    && item.DeliveryId == remoteEvent.DeliveryId,
                cancellationToken))
        {
            return new RemoteProviderEventResult(true, true, 0, "duplicate");
        }

        var now = _timeProvider.GetUtcNow();
        var mirrors = await dbContext.RepositoryMirrors
            .Include(item => item.Job)
            .Where(item => item.ConnectionId == remoteEvent.ConnectionId
                && item.RemoteRepositoryId == repositoryEvent.ExternalId)
            .ToArrayAsync(cancellationToken);
        if (repositoryEvent.Deleted)
        {
            foreach (var mirror in mirrors)
            {
                mirror.IsEnabled = false;
                mirror.Status = RemoteMirrorStatus.Failed;
                mirror.LastErrorCode = RemoteRepositorySyncErrorCodes.RepositoryNotFound;
                mirror.UpdatedAtUtc = now.UtcDateTime;
                AddAudit(dbContext, mirror, "mirror.remote.deleted", "failed", now);
            }
        }
        connection.Status = RemoteConnectionStatus.Healthy;
        connection.LastErrorCode = null;
        connection.UpdatedAtUtc = now.UtcDateTime;

        var enqueued = 0;
        foreach (var mirror in mirrors)
        {
            if (repositoryEvent.Deleted)
            {
                if (mirror.Job is not null)
                {
                    _ = await _jobQueue.RequestCancellationAsync(mirror.Job.Id, cancellationToken);
                }
                continue;
            }

            if (repositoryEvent.Profile is not null)
            {
                var profile = repositoryEvent.Profile;
                profile = new RemoteRepositoryProfile(
                    new RemoteRepositoryIdentity(
                        remoteEvent.Provider,
                        connection.ServerUrl,
                        repositoryEvent.ExternalId),
                    profile.OwnerLogin,
                    profile.Name,
                    profile.FullName,
                    profile.WebUrl,
                    profile.IsPrivate,
                    profile.DefaultBranch);
                if (!await _mirrorService.UpdateRemoteProfileAsync(
                    mirror.Id,
                    profile,
                    cancellationToken))
                {
                    return new RemoteProviderEventResult(false, false, enqueued, "invalid_repository");
                }
            }
            if (mirror.Direction == RemoteMirrorDirection.Pull && mirror.IsEnabled)
            {
                await _jobQueue.EnqueueAsync(
                    mirror.Id,
                    RemoteMirrorJobTrigger.Webhook,
                    cancellationToken: cancellationToken);
                enqueued++;
            }
        }

        dbContext.RemoteProviderEvents.Add(new GitCandyRemoteProviderEvent
        {
            ConnectionId = remoteEvent.ConnectionId,
            Provider = remoteEvent.Provider,
            DeliveryId = remoteEvent.DeliveryId,
            EventType = remoteEvent.EventType,
            PayloadHash = Convert.ToHexString(SHA256.HashData(remoteEvent.Payload.Span)),
            ReceivedAtUtc = now.UtcDateTime
        });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await IsDuplicateAsync(remoteEvent, cancellationToken))
            {
                return new RemoteProviderEventResult(true, true, 0, "duplicate");
            }
            throw;
        }

        return new RemoteProviderEventResult(true, false, enqueued, repositoryEvent.Deleted ? "remote_deleted" : "accepted");
    }

    private async Task<bool> IsDuplicateAsync(
        RemoteProviderEvent remoteEvent,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.RemoteProviderEvents.AsNoTracking().AnyAsync(
            item => item.ConnectionId == remoteEvent.ConnectionId
                && item.DeliveryId == remoteEvent.DeliveryId,
            cancellationToken);
    }

    private static ProviderRepositoryEvent Parse(
        RemoteProviderKind provider,
        string eventType,
        ReadOnlySpan<byte> payload)
    {
        using var document = JsonDocument.Parse(payload.ToArray());
        var root = document.RootElement;
        var repository = provider == RemoteProviderKind.GitLab
            ? GetObject(root, "project") ?? GetObject(root, "repository")
            : GetObject(root, "repository");
        if (repository is not JsonElement repositoryElement)
        {
            throw new JsonException("The provider event has no repository object.");
        }

        var externalId = RequiredId(repositoryElement, "id");
        var action = OptionalString(root, "action");
        var deleted = string.Equals(action, "deleted", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("delete", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("destroy", StringComparison.OrdinalIgnoreCase);
        if (deleted)
        {
            return new ProviderRepositoryEvent(externalId, true, null);
        }

        var fullName = OptionalString(repositoryElement, "full_name")
            ?? OptionalString(repositoryElement, "path_with_namespace")
            ?? OptionalString(repositoryElement, "name_with_namespace");
        var webUrl = OptionalString(repositoryElement, "html_url")
            ?? OptionalString(repositoryElement, "web_url");
        if (string.IsNullOrWhiteSpace(fullName)
            || string.IsNullOrWhiteSpace(webUrl)
            || !Uri.TryCreate(webUrl, UriKind.Absolute, out var uri))
        {
            return new ProviderRepositoryEvent(externalId, false, null);
        }
        var separator = fullName.LastIndexOf('/');
        if (separator <= 0 || separator == fullName.Length - 1)
        {
            return new ProviderRepositoryEvent(externalId, false, null);
        }

        var profile = new RemoteRepositoryProfile(
            new RemoteRepositoryIdentity(provider, new Uri(uri.GetLeftPart(UriPartial.Authority)).AbsoluteUri, externalId),
            fullName[..separator],
            fullName[(separator + 1)..],
            fullName,
            uri,
            false,
            OptionalString(repositoryElement, "default_branch"));
        return new ProviderRepositoryEvent(externalId, false, profile);
    }

    private static JsonElement? GetObject(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Object
            ? property
            : null;

    private static string RequiredId(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            throw new JsonException("The provider repository has no stable ID.");
        }
        return property.ValueKind switch
        {
            JsonValueKind.String when !string.IsNullOrWhiteSpace(property.GetString()) => property.GetString()!,
            JsonValueKind.Number when property.TryGetInt64(out var value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new JsonException("The provider repository stable ID is invalid.")
        };
    }

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static void AddAudit(
        GitCandyDbContext dbContext,
        GitCandyRepositoryMirror mirror,
        string action,
        string outcome,
        DateTimeOffset occurredAt)
    {
        dbContext.GovernanceAuditEvents.Add(new GitCandyGovernanceAuditEvent
        {
            RepositoryId = mirror.RepositoryId,
            Action = action,
            Outcome = outcome,
            ReferenceName = string.Empty,
            Detail = $"mirror={mirror.Id}",
            OccurredAtUtc = occurredAt.UtcDateTime
        });
    }

    private sealed record ProviderRepositoryEvent(
        string ExternalId,
        bool Deleted,
        RemoteRepositoryProfile? Profile);
}
