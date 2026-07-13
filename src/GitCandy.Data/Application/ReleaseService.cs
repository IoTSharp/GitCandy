using GitCandy.Configuration;
using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Integrations;
using GitCandy.Issues;
using GitCandy.Releases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace GitCandy.Application;

internal sealed class ReleaseService(
    GitCandyDbContext dbContext,
    IReleaseTagResolver tagResolver,
    IReleaseAssetStore assetStore,
    IIssueMarkdownRenderer markdownRenderer,
    IIntegrationEventPublisher integrationEventPublisher,
    IOptions<ReleaseOptions> options,
    TimeProvider timeProvider,
    Microsoft.Extensions.Logging.ILogger<ReleaseService> logger) : IReleaseService
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly IReleaseTagResolver _tagResolver = tagResolver;
    private readonly IReleaseAssetStore _assetStore = assetStore;
    private readonly IIssueMarkdownRenderer _markdownRenderer = markdownRenderer;
    private readonly IIntegrationEventPublisher _integrationEventPublisher = integrationEventPublisher;
    private readonly ReleaseOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly Microsoft.Extensions.Logging.ILogger<ReleaseService> _logger = logger;

    public async Task<IReadOnlyList<ReleaseDetails>> GetReleasesAsync(
        long repositoryId,
        bool includeDrafts,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Releases.AsNoTracking()
            .Include(item => item.Assets)
            .Where(item => item.RepositoryId == repositoryId);
        if (!includeDrafts) query = query.Where(item => !item.IsDraft);
        var releases = await query.OrderByDescending(item => item.PublishedAtUtc ?? item.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        return await MapAsync(releases, cancellationToken);
    }

    public async Task<ReleaseDetails?> GetReleaseAsync(
        long repositoryId,
        long releaseId,
        bool includeDrafts,
        CancellationToken cancellationToken = default)
    {
        var release = await _dbContext.Releases.AsNoTracking().Include(item => item.Assets)
            .SingleOrDefaultAsync(item => item.Id == releaseId && item.RepositoryId == repositoryId
                && (includeDrafts || !item.IsDraft), cancellationToken);
        return release is null ? null : (await MapAsync([release], cancellationToken))[0];
    }

    public async Task<ReleaseDetails?> CreateAsync(
        long repositoryId,
        string actorUserId,
        CreateRelease command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentNullException.ThrowIfNull(command);
        var tagName = command.TagName.Trim();
        var name = command.Name.Trim();
        var body = command.BodyMarkdown.Trim();
        if (tagName.Length is 0 or > SchemaLimits.GitRefName
            || name.Length is 0 or > SchemaLimits.ReleaseName
            || body.Length > SchemaLimits.IssueBody
            || tagName.Any(static character => char.IsControl(character))) return null;
        var repository = await (
            from item in _dbContext.Repositories.AsNoTracking()
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking()
                on item.NamespaceId equals repositoryNamespace.Id
            where item.Id == repositoryId
            select new { Repository = item, Namespace = repositoryNamespace.Slug })
            .SingleOrDefaultAsync(cancellationToken);
        if (repository is null) return null;
        var tagCommitSha = _tagResolver.ResolveTag(repository.Repository.StorageName, tagName, cancellationToken);
        if (tagCommitSha is null) return null;
        var now = _timeProvider.GetUtcNow();
        var release = new GitCandyRelease
        {
            RepositoryId = repositoryId,
            TagName = tagName,
            NormalizedTagName = tagName.ToUpperInvariant(),
            TagCommitSha = tagCommitSha,
            Name = name,
            BodyMarkdown = body,
            BodyHtml = _markdownRenderer.Render(body, repository.Namespace, repository.Repository.Name),
            IsDraft = command.IsDraft,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = now.UtcDateTime,
            PublishedAtUtc = command.IsDraft ? null : now.UtcDateTime
        };
        _dbContext.Releases.Add(release);
        AddAudit(repositoryId, actorUserId, command.IsDraft ? "release.create-draft" : "release.publish",
            tagName, tagCommitSha, now);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return null;
        }
        if (!command.IsDraft)
        {
            try
            {
                await _integrationEventPublisher.PublishReleasePublishedAsync(
                    repositoryId,
                    actorUserId,
                    release.Id,
                    tagName,
                    tagCommitSha,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception,
                    "Release {ReleaseId} was committed but its integration event will be retried by workspace projection.",
                    release.Id);
            }
        }
        return await GetReleaseAsync(repositoryId, release.Id, includeDrafts: true, cancellationToken);
    }

    public async Task<ReleaseAsset?> AddAssetAsync(
        long repositoryId,
        long releaseId,
        string actorUserId,
        string fileName,
        string contentType,
        Stream content,
        long? declaredLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        ArgumentNullException.ThrowIfNull(content);
        var normalizedFileName = fileName.Trim();
        var normalizedContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
        if (normalizedFileName.Length is 0 or > SchemaLimits.ReleaseAssetName
            || !string.Equals(Path.GetFileName(normalizedFileName), normalizedFileName, StringComparison.Ordinal)
            || normalizedFileName.Any(static character => char.IsControl(character))
            || normalizedContentType.Length > SchemaLimits.ReleaseContentType
            || normalizedContentType.Any(static character => character is '\r' or '\n')
            || declaredLength is < 0
            || declaredLength > _options.MaxAssetBytes) return null;
        var release = await _dbContext.Releases.Include(item => item.Assets)
            .SingleOrDefaultAsync(item => item.Id == releaseId && item.RepositoryId == repositoryId, cancellationToken);
        if (release is null
            || release.Assets.Count >= _options.MaxAssetsPerRelease
            || release.Assets.Sum(item => item.Length) + (declaredLength ?? 0) > _options.MaxTotalAssetBytes
            || release.Assets.Any(item => string.Equals(item.FileName, normalizedFileName, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        var assetId = Guid.NewGuid().ToString("N");
        var remainingTotal = _options.MaxTotalAssetBytes - release.Assets.Sum(item => item.Length);
        var stored = await _assetStore.StoreAsync(
            repositoryId,
            releaseId,
            assetId,
            content,
            Math.Min(_options.MaxAssetBytes, remainingTotal),
            cancellationToken);
        if (stored is null) return null;
        var now = _timeProvider.GetUtcNow();
        var asset = new GitCandyReleaseAsset
        {
            Id = assetId,
            ReleaseId = releaseId,
            FileName = normalizedFileName,
            ContentType = normalizedContentType,
            Length = stored.Length,
            Sha256 = stored.Sha256,
            CreatedAtUtc = now.UtcDateTime
        };
        _dbContext.ReleaseAssets.Add(asset);
        AddAudit(repositoryId, actorUserId, "release.asset-upload", release.TagName,
            $"{normalizedFileName}:{stored.Length}:{stored.Sha256}", now);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ToAsset(asset);
        }
        catch (DbUpdateException)
        {
            await _assetStore.DeleteAsync(repositoryId, releaseId, assetId, cancellationToken);
            return null;
        }
    }

    public async Task<ReleaseAssetDownload?> OpenAssetAsync(
        long repositoryId,
        string assetId,
        bool includeDrafts,
        CancellationToken cancellationToken = default)
    {
        var asset = await _dbContext.ReleaseAssets.AsNoTracking().Include(item => item.Release)
            .SingleOrDefaultAsync(item => item.Id == assetId
                && item.Release!.RepositoryId == repositoryId
                && (includeDrafts || !item.Release.IsDraft), cancellationToken);
        if (asset?.Release is null) return null;
        var content = await _assetStore.OpenReadAsync(repositoryId, asset.ReleaseId, asset.Id, cancellationToken);
        if (content is null) return null;
        await _dbContext.ReleaseAssets.Where(item => item.Id == asset.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DownloadCount, item => item.DownloadCount + 1), cancellationToken);
        return new ReleaseAssetDownload(ToAsset(asset), content);
    }

    public async Task<bool> DeleteAssetAsync(
        long repositoryId,
        string assetId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);
        var asset = await _dbContext.ReleaseAssets.Include(item => item.Release)
            .SingleOrDefaultAsync(item => item.Id == assetId && item.Release!.RepositoryId == repositoryId, cancellationToken);
        if (asset?.Release is null) return false;
        var now = _timeProvider.GetUtcNow();
        _dbContext.ReleaseAssets.Remove(asset);
        AddAudit(repositoryId, actorUserId, "release.asset-delete", asset.Release.TagName, asset.FileName, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _assetStore.DeleteAsync(repositoryId, asset.ReleaseId, asset.Id, cancellationToken);
        return true;
    }

    public async Task<int> CleanupOrphansAsync(CancellationToken cancellationToken = default)
    {
        var active = (await _dbContext.ReleaseAssets.AsNoTracking().Select(item => item.Id)
            .ToArrayAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        return await _assetStore.DeleteOrphansAsync(
            active,
            _timeProvider.GetUtcNow().Subtract(_options.OrphanRetention),
            cancellationToken);
    }

    private async Task<IReadOnlyList<ReleaseDetails>> MapAsync(
        IReadOnlyList<GitCandyRelease> releases,
        CancellationToken cancellationToken)
    {
        var userIds = releases.Select(item => item.CreatedByUserId).Distinct(StringComparer.Ordinal).ToArray();
        var users = await _dbContext.Users.AsNoTracking().Where(item => userIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.UserName ?? item.Id, cancellationToken);
        return releases.Select(item => new ReleaseDetails(
            item.Id,
            item.RepositoryId,
            item.TagName,
            item.TagCommitSha,
            item.Name,
            item.BodyMarkdown,
            item.BodyHtml,
            item.IsDraft,
            users.GetValueOrDefault(item.CreatedByUserId, "deleted-user"),
            ToDateTimeOffset(item.CreatedAtUtc),
            item.PublishedAtUtc is null ? null : ToDateTimeOffset(item.PublishedAtUtc.Value),
            item.Assets.OrderBy(asset => asset.FileName, StringComparer.OrdinalIgnoreCase).Select(ToAsset).ToArray()))
            .ToArray();
    }

    private void AddAudit(
        long repositoryId,
        string actorUserId,
        string action,
        string reference,
        string detail,
        DateTimeOffset occurredAt)
    {
        _dbContext.GovernanceAuditEvents.Add(new GitCandyGovernanceAuditEvent
        {
            RepositoryId = repositoryId,
            ActorUserId = actorUserId,
            Action = action,
            Outcome = "success",
            ReferenceName = reference,
            Detail = detail.Length <= SchemaLimits.AuditDetail ? detail : detail[..SchemaLimits.AuditDetail],
            OccurredAtUtc = occurredAt.UtcDateTime
        });
    }

    private static ReleaseAsset ToAsset(GitCandyReleaseAsset item) => new(
        item.Id,
        item.FileName,
        item.ContentType,
        item.Length,
        item.Sha256,
        item.DownloadCount,
        ToDateTimeOffset(item.CreatedAtUtc));

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
