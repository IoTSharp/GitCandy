using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Workspace;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class WorkspaceActivityPublisher(GitCandyDbContext dbContext, TimeProvider timeProvider) : IWorkspaceActivityPublisher
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task PublishPushAsync(string repositoryStorageName, string actorName, string repositoryStateId, CancellationToken cancellationToken = default)
    {
        var repository = await (from item in _dbContext.Repositories.AsNoTracking()
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on item.NamespaceId equals repositoryNamespace.Id
            where item.StorageName == repositoryStorageName
            select new { Repository = item, Namespace = repositoryNamespace.Slug }).SingleOrDefaultAsync(cancellationToken);
        if (repository is null || repositoryStateId.Length is < 16 or > 64) return;
        var eventId = $"push:{repository.Repository.Id}:{repositoryStateId}";
        if (await _dbContext.ActivityEvents.AnyAsync(item => item.EventId == eventId, cancellationToken)) return;
        var normalizedActor = actorName.Trim().ToUpperInvariant();
        var actorUserId = await _dbContext.Users.AsNoTracking().Where(item => item.NormalizedUserName == normalizedActor)
            .Select(item => item.Id).SingleOrDefaultAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _dbContext.ActivityEvents.Add(new GitCandyActivityEvent
        {
            EventId = eventId,
            ActorUserId = actorUserId,
            RepositoryId = repository.Repository.Id,
            ResourceType = WorkspaceResourceType.Repository,
            ResourceId = $"repository:{repository.Repository.Id}",
            Type = WorkspaceActivityType.Push,
            Title = $"Pushed to {repository.Namespace}/{repository.Repository.Name}",
            Url = $"/{repository.Namespace}/{repository.Repository.Name}",
            OccurredAtUtc = now,
            RetainUntilUtc = now.AddDays(180)
        });
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { _dbContext.ChangeTracker.Clear(); }
    }
}
