using GitCandy.Data;
using GitCandy.Data.Domain;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

/// <summary>基于稳定 ID 和名称占用表的统一仓库地址解析器。</summary>
internal sealed class RepositoryAddressResolver(
    GitCandyDbContext dbContext,
    TimeProvider timeProvider) : IRepositoryAddressResolver
{
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<RepositoryAddressResolution?> ResolveAsync(
        string namespaceSlug,
        string repositorySlug,
        CancellationToken cancellationToken = default)
    {
        if (!NamespaceSlugRules.IsValidNamespaceSlug(namespaceSlug)
            || !NamespaceSlugRules.IsValidRepositorySlug(repositorySlug))
        {
            return null;
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var normalizedNamespace = NamespaceSlugRules.Normalize(namespaceSlug);
        var namespaceClaim = await _dbContext.NamespaceClaims
            .AsNoTracking()
            .Where(claim => claim.NormalizedSlug == normalizedNamespace)
            .Select(claim => new
            {
                claim.ClaimType,
                claim.NamespaceId,
                AliasNamespaceId = claim.NamespaceAlias == null
                    || claim.NamespaceAlias.ReleasedAtUtc != null
                    || claim.NamespaceAlias.ExpiresAtUtc <= utcNow
                    ? null
                    : (long?)claim.NamespaceAlias.NamespaceId
            })
            .SingleOrDefaultAsync(cancellationToken);
        var namespaceId = namespaceClaim?.NamespaceId ?? namespaceClaim?.AliasNamespaceId;
        if (namespaceId is null || namespaceClaim?.ClaimType == NameClaimType.Reserved)
        {
            return null;
        }

        var normalizedRepository = NamespaceSlugRules.Normalize(repositorySlug);
        var repositoryClaim = await _dbContext.RepositoryClaims
            .AsNoTracking()
            .Where(claim => claim.NamespaceId == namespaceId.Value
                && claim.NormalizedSlug == normalizedRepository)
            .Select(claim => new
            {
                claim.ClaimType,
                claim.RepositoryId,
                AliasRepositoryId = claim.RepositoryAlias == null
                    || claim.RepositoryAlias.ReleasedAtUtc != null
                    || claim.RepositoryAlias.ExpiresAtUtc <= utcNow
                    ? null
                    : (long?)claim.RepositoryAlias.RepositoryId
            })
            .SingleOrDefaultAsync(cancellationToken);
        var repositoryId = repositoryClaim?.RepositoryId ?? repositoryClaim?.AliasRepositoryId;
        return repositoryId is null
            ? null
            : await CreateResolutionAsync(
                repositoryId.Value,
                usedNamespaceAlias: namespaceClaim!.ClaimType == NameClaimType.Alias,
                usedRepositoryAlias: repositoryClaim!.ClaimType == NameClaimType.Alias,
                usedLegacyRoute: false,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RepositoryAddressResolution?> ResolveLegacyAsync(
        string project,
        CancellationToken cancellationToken = default)
    {
        if (!NamespaceSlugRules.IsValidRepositorySlug(project))
        {
            return null;
        }

        var normalizedProject = NamespaceSlugRules.Normalize(project);
        var repositoryId = await _dbContext.LegacyRepositoryRoutes
            .AsNoTracking()
            .Where(route => route.NormalizedProject == normalizedProject)
            .Select(route => (long?)route.RepositoryId)
            .SingleOrDefaultAsync(cancellationToken);
        if (repositoryId is null)
        {
            var fallbackIds = await _dbContext.Repositories
                .AsNoTracking()
                .Where(repository => repository.NormalizedName == normalizedProject)
                .Select(repository => repository.Id)
                .Take(2)
                .ToArrayAsync(cancellationToken);
            repositoryId = fallbackIds.Length == 1 ? fallbackIds[0] : null;
        }
        return repositoryId is null
            ? null
            : await CreateResolutionAsync(
                repositoryId.Value,
                usedNamespaceAlias: false,
                usedRepositoryAlias: false,
                usedLegacyRoute: true,
                cancellationToken);
    }

    private Task<RepositoryAddressResolution?> CreateResolutionAsync(
        long repositoryId,
        bool usedNamespaceAlias,
        bool usedRepositoryAlias,
        bool usedLegacyRoute,
        CancellationToken cancellationToken)
    {
        return _dbContext.Repositories
            .AsNoTracking()
            .Where(repository => repository.Id == repositoryId)
            .Select(repository => new RepositoryAddressResolution(
                repository.Id,
                repository.NamespaceId,
                repository.Namespace!.Slug,
                repository.Name,
                repository.StorageName,
                repository.IsPrivate,
                usedNamespaceAlias,
                usedRepositoryAlias,
                usedLegacyRoute))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
