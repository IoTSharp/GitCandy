using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Search;
using Microsoft.EntityFrameworkCore;

namespace GitCandy.Application;

internal sealed class CollaborationSearchService(GitCandyDbContext dbContext) : ICollaborationSearchService
{
    private readonly GitCandyDbContext _dbContext = dbContext;

    public async Task<DatabaseSearchResult> SearchAsync(
        string? userId,
        bool isAdministrator,
        string query,
        SearchScope scope,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var text = query.Trim();
        if (text.Length is < 2 or > 200 || !Enum.IsDefined(scope))
        {
            return new DatabaseSearchResult([], []);
        }
        var boundedLimit = Math.Clamp(limit, 1, 200);
        var readable = ReadableRepositories(userId, isAdministrator);
        var candidates = await (
            from repository in readable
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking()
                on repository.NamespaceId equals repositoryNamespace.Id
            orderby repositoryNamespace.Slug, repository.NormalizedName
            select new SearchRepositoryCandidate(
                repository.Id,
                repositoryNamespace.Slug,
                repository.Name,
                repository.StorageName,
                repository.Description))
            .Take(25)
            .ToArrayAsync(cancellationToken);
        var hits = new List<SearchHit>(boundedLimit);
        if (scope is SearchScope.All or SearchScope.Repository)
        {
            var normalized = text.ToUpperInvariant();
            var repositoryRows = await (
                from repository in readable
                join repositoryNamespace in _dbContext.Namespaces.AsNoTracking()
                    on repository.NamespaceId equals repositoryNamespace.Id
                where repository.NormalizedName.Contains(normalized)
                    || repositoryNamespace.NormalizedSlug.Contains(normalized)
                    || repository.Description.Contains(text)
                orderby repositoryNamespace.Slug, repository.NormalizedName
                select new SearchRepositoryCandidate(
                    repository.Id,
                    repositoryNamespace.Slug,
                    repository.Name,
                    repository.StorageName,
                    repository.Description))
                .Take(boundedLimit)
                .ToArrayAsync(cancellationToken);
            hits.AddRange(repositoryRows.Select(item => new SearchHit(
                    SearchScope.Repository,
                    item.RepositoryId,
                    $"{item.NamespaceSlug}/{item.RepositorySlug}",
                    $"{item.NamespaceSlug}/{item.RepositorySlug}",
                    item.Description,
                    $"/{item.NamespaceSlug}/{item.RepositorySlug}")));
        }
        if (scope is SearchScope.All or SearchScope.Issue)
        {
            var issueRows = await (
                from issue in _dbContext.Issues.AsNoTracking()
                join repository in readable on issue.RepositoryId equals repository.Id
                join repositoryNamespace in _dbContext.Namespaces.AsNoTracking()
                    on repository.NamespaceId equals repositoryNamespace.Id
                where issue.Title.Contains(text) || issue.BodyMarkdown.Contains(text)
                orderby issue.UpdatedAtUtc descending
                select new { Issue = issue, Repository = repository, Namespace = repositoryNamespace.Slug })
                .Take(boundedLimit)
                .ToArrayAsync(cancellationToken);
            hits.AddRange(issueRows.Select(row => new SearchHit(
                SearchScope.Issue,
                row.Repository.Id,
                $"{row.Namespace}/{row.Repository.Name}",
                $"#{row.Issue.Number} {row.Issue.Title}",
                Truncate(row.Issue.BodyMarkdown, 500),
                $"/{row.Namespace}/{row.Repository.Name}/issues/{row.Issue.Number}",
                ToDateTimeOffset(row.Issue.UpdatedAtUtc))));
        }
        if (scope is SearchScope.All or SearchScope.PullRequest)
        {
            var pullRequestRows = await (
                from pullRequest in _dbContext.PullRequests.AsNoTracking()
                join repository in readable on pullRequest.RepositoryId equals repository.Id
                join repositoryNamespace in _dbContext.Namespaces.AsNoTracking()
                    on repository.NamespaceId equals repositoryNamespace.Id
                where pullRequest.Title.Contains(text) || pullRequest.BodyMarkdown.Contains(text)
                orderby pullRequest.UpdatedAtUtc descending
                select new { PullRequest = pullRequest, Repository = repository, Namespace = repositoryNamespace.Slug })
                .Take(boundedLimit)
                .ToArrayAsync(cancellationToken);
            hits.AddRange(pullRequestRows.Select(row => new SearchHit(
                SearchScope.PullRequest,
                row.Repository.Id,
                $"{row.Namespace}/{row.Repository.Name}",
                $"#{row.PullRequest.Number} {row.PullRequest.Title}",
                Truncate(row.PullRequest.BodyMarkdown, 500),
                $"/{row.Namespace}/{row.Repository.Name}/pulls/{row.PullRequest.Number}",
                ToDateTimeOffset(row.PullRequest.UpdatedAtUtc))));
        }
        return new DatabaseSearchResult(
            hits.OrderByDescending(item => item.UpdatedAt).Take(boundedLimit).ToArray(),
            candidates);
    }

    private IQueryable<GitCandyRepository> ReadableRepositories(string? userId, bool isAdministrator)
    {
        var repositories = _dbContext.Repositories.AsNoTracking();
        if (isAdministrator) return repositories;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return repositories.Where(item => !item.IsPrivate && item.AllowAnonymousRead);
        }
        return repositories.Where(repository =>
            (!repository.IsPrivate && repository.AllowAnonymousRead)
            || _dbContext.UserRepositoryRoles.Any(role => role.RepositoryId == repository.Id
                && role.UserId == userId && role.AllowRead)
            || _dbContext.TeamRepositoryRoles.Any(role => role.RepositoryId == repository.Id && role.AllowRead
                && _dbContext.UserTeamRoles.Any(member => member.TeamId == role.TeamId && member.UserId == userId)));
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
