using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Integrations;
using GitCandy.PullRequests;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class IntegrationEventPublisher(
    IDbContextFactory<GitCandyDbContext> dbContextFactory,
    TimeProvider timeProvider) : IIntegrationEventPublisher
{
    private const int MaxPushReferences = 64;
    private readonly IDbContextFactory<GitCandyDbContext> _dbContextFactory = dbContextFactory;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task PublishPushAsync(
        string repositoryStorageName,
        string actorName,
        string repositoryStateId,
        IReadOnlyList<IntegrationReference> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryStorageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryStateId);
        ArgumentNullException.ThrowIfNull(references);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var repository = await ResolveRepositoryByStorageAsync(dbContext, repositoryStorageName, cancellationToken);
        if (repository is null) return;
        var actorUserId = await ResolveActorUserIdAsync(dbContext, actorName, cancellationToken);
        var occurredAt = _timeProvider.GetUtcNow();
        var eventId = CreateEventId($"push:{repository.Id}:{repositoryStateId}");
        var boundedReferences = references.Take(MaxPushReferences).Select(item => new
        {
            name = item.Name,
            targetSha = item.TargetSha
        }).ToArray();
        var data = new
        {
            repositoryStateId,
            references = boundedReferences,
            truncated = references.Count > boundedReferences.Length
        };
        await AddEventAsync(
            dbContext,
            repository,
            eventId,
            "push",
            WebhookEventTypes.Push,
            actorUserId,
            actorName,
            occurredAt,
            data,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task PublishPullRequestMergedAsync(
        PullRequestMergedEvent mergedEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mergedEvent);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var repository = await ResolveRepositoryByIdAsync(
            dbContext,
            mergedEvent.Context.RepositoryId,
            cancellationToken);
        if (repository is null) return;
        var actorName = await dbContext.Users.AsNoTracking()
            .Where(item => item.Id == mergedEvent.Context.ActorUserId)
            .Select(item => item.UserName)
            .SingleOrDefaultAsync(cancellationToken) ?? mergedEvent.Context.ActorUserId;
        var eventId = CreateEventId(
            $"pull-request.merged:{repository.Id}:{mergedEvent.Context.PullRequestNumber}:{mergedEvent.CommitSha}");
        var data = new
        {
            number = mergedEvent.Context.PullRequestNumber,
            sourceBranch = mergedEvent.Context.SourceBranch,
            targetBranch = mergedEvent.Context.TargetBranch,
            baseSha = mergedEvent.Context.BaseSha,
            headSha = mergedEvent.Context.HeadSha,
            mergeCommitSha = mergedEvent.CommitSha,
            method = mergedEvent.Context.Method.ToString()
        };
        await AddEventAsync(
            dbContext,
            repository,
            eventId,
            "pull_request.merged",
            WebhookEventTypes.PullRequestMerged,
            mergedEvent.Context.ActorUserId,
            actorName,
            mergedEvent.MergedAt,
            data,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task PublishCheckUpdatedAsync(
        long repositoryId,
        string actorUserId,
        CommitCheckSummary check,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentNullException.ThrowIfNull(check);
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await AddCheckUpdatedAsync(dbContext, repositoryId, actorUserId, check, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    internal static async Task AddCheckUpdatedAsync(
        GitCandyDbContext dbContext,
        long repositoryId,
        string actorUserId,
        CommitCheckSummary check,
        CancellationToken cancellationToken)
    {
        var repository = await ResolveRepositoryByIdAsync(dbContext, repositoryId, cancellationToken);
        if (repository is null) return;
        var actorName = await dbContext.Users.AsNoTracking()
            .Where(item => item.Id == actorUserId)
            .Select(item => item.UserName)
            .SingleOrDefaultAsync(cancellationToken) ?? actorUserId;
        var eventId = CreateEventId(
            $"check.updated:{repositoryId}:{check.Sha}:{check.Kind}:{check.Context}:{check.UpdatedAt.UtcTicks}");
        var data = new
        {
            sha = check.Sha,
            kind = check.Kind.ToString(),
            context = check.Context,
            state = check.State.ToString(),
            description = check.Description,
            targetUrl = check.TargetUrl,
            externalId = check.ExternalId
        };
        await AddEventAsync(
            dbContext,
            repository,
            eventId,
            "check.updated",
            WebhookEventTypes.CheckUpdated,
            actorUserId,
            actorName,
            check.UpdatedAt,
            data,
            cancellationToken);
    }

    private static async Task AddEventAsync<TData>(
        GitCandyDbContext dbContext,
        RepositoryEnvelope repository,
        string eventId,
        string eventType,
        WebhookEventTypes subscriptionEvent,
        string? actorUserId,
        string actorName,
        DateTimeOffset occurredAt,
        TData data,
        CancellationToken cancellationToken)
    {
        if (await dbContext.IntegrationEvents.AsNoTracking().AnyAsync(item => item.Id == eventId, cancellationToken))
        {
            return;
        }
        var payload = JsonSerializer.Serialize(new
        {
            version = 1,
            id = eventId,
            type = eventType,
            occurredAt,
            repository = new
            {
                id = repository.Id,
                @namespace = repository.NamespaceSlug,
                name = repository.Name,
                fullName = $"{repository.NamespaceSlug}/{repository.Name}",
                url = $"/{repository.NamespaceSlug}/{repository.Name}"
            },
            actor = new { id = actorUserId, name = actorName },
            data
        });
        if (payload.Length > SchemaLimits.IntegrationPayload)
        {
            return;
        }
        dbContext.IntegrationEvents.Add(new GitCandyIntegrationEvent
        {
            Id = eventId,
            RepositoryId = repository.Id,
            SchemaVersion = 1,
            Type = eventType,
            ActorUserId = actorUserId,
            ActorName = actorName,
            PayloadJson = payload,
            OccurredAtUtc = occurredAt.UtcDateTime
        });
        var subscriptionIds = await dbContext.WebhookSubscriptions.AsNoTracking()
            .Where(item => item.RepositoryId == repository.Id
                && item.IsActive
                && (item.Events & subscriptionEvent) == subscriptionEvent)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var subscriptionId in subscriptionIds)
        {
            dbContext.WebhookDeliveries.Add(new GitCandyWebhookDelivery
            {
                Id = Guid.NewGuid().ToString("N"),
                SubscriptionId = subscriptionId,
                EventId = eventId,
                State = WebhookDeliveryState.Pending,
                NextAttemptAtUtc = occurredAt.UtcDateTime,
                CreatedAtUtc = occurredAt.UtcDateTime
            });
        }
    }

    private static Task<RepositoryEnvelope?> ResolveRepositoryByStorageAsync(
        GitCandyDbContext dbContext,
        string storageName,
        CancellationToken cancellationToken) =>
        (from repository in dbContext.Repositories.AsNoTracking()
            join repositoryNamespace in dbContext.Namespaces.AsNoTracking()
                on repository.NamespaceId equals repositoryNamespace.Id
            where repository.StorageName == storageName
            select new RepositoryEnvelope(repository.Id, repositoryNamespace.Slug, repository.Name))
        .SingleOrDefaultAsync(cancellationToken);

    private static Task<RepositoryEnvelope?> ResolveRepositoryByIdAsync(
        GitCandyDbContext dbContext,
        long repositoryId,
        CancellationToken cancellationToken) =>
        (from repository in dbContext.Repositories.AsNoTracking()
            join repositoryNamespace in dbContext.Namespaces.AsNoTracking()
                on repository.NamespaceId equals repositoryNamespace.Id
            where repository.Id == repositoryId
            select new RepositoryEnvelope(repository.Id, repositoryNamespace.Slug, repository.Name))
        .SingleOrDefaultAsync(cancellationToken);

    private static async Task<string?> ResolveActorUserIdAsync(
        GitCandyDbContext dbContext,
        string actorName,
        CancellationToken cancellationToken)
    {
        var normalized = actorName.Trim().ToUpperInvariant();
        return await dbContext.Users.AsNoTracking()
            .Where(item => item.NormalizedUserName == normalized)
            .Select(item => item.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static string CreateEventId(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    private sealed record RepositoryEnvelope(long Id, string NamespaceSlug, string Name);
}
