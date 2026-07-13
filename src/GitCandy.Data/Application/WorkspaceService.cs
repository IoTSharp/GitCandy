using GitCandy.Data;
using GitCandy.Data.Domain;
using GitCandy.Data.Permissions;
using GitCandy.Issues;
using GitCandy.PullRequests;
using GitCandy.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GitCandy.Application;

internal sealed class WorkspaceService(
    GitCandyDbContext dbContext,
    IGitCandyRepositoryPermissionQuery permissionQuery,
    TimeProvider timeProvider,
    ILogger<WorkspaceService> logger) : IWorkspaceService
{
    private const int MaximumPageSize = 100;
    private const string RecommendationVersion = "m12.7-v1";
    private readonly GitCandyDbContext _dbContext = dbContext;
    private readonly IGitCandyRepositoryPermissionQuery _permissionQuery = permissionQuery;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<WorkspaceService> _logger = logger;
    private readonly HashSet<string> _synchronizedUsers = new(StringComparer.Ordinal);

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<WorkspaceDashboard> GetDashboardAsync(string userId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var todos = await LoadModuleAsync("todos", async () => (IReadOnlyList<WorkspaceTodo>)(await GetTodosAsync(userId, 1, 8, cancellationToken: cancellationToken)).Items, []);
        var feed = await LoadModuleAsync("feed", async () => (IReadOnlyList<WorkspaceActivity>)(await GetFeedAsync(userId, isAdministrator, 1, 12, cancellationToken)).Items, []);
        var notifications = await LoadModuleAsync("notifications", async () => (IReadOnlyList<WorkspaceNotification>)(await GetNotificationsAsync(userId, isAdministrator, new WorkspaceNotificationQuery(PageSize: 6), cancellationToken)).Items, []);
        var repositories = await LoadModuleAsync("attention repositories", () => GetAttentionRepositoriesAsync(userId, isAdministrator, 8, cancellationToken), []);
        var teams = await LoadModuleAsync("teams", () => GetTeamsAsync(userId, cancellationToken), []);
        var recommendations = await LoadModuleAsync("recommendations", async () => (IReadOnlyList<WorkspaceRepository>)(await ExploreAsync(new ExploreQuery(PageSize: 6), cancellationToken)).Repositories.Items, []);
        var unread = await LoadValueAsync("unread notification count", () => CountVisibleUnreadAsync(userId, isAdministrator, cancellationToken), 0);
        return new WorkspaceDashboard(
            todos,
            feed,
            repositories,
            notifications,
            teams,
            recommendations,
            unread);
    }

    public async Task<WorkspacePage<WorkspaceTodo>> GetTodosAsync(string userId, int page, int pageSize, bool includeCompleted = false, CancellationToken cancellationToken = default)
    {
        await EnsureUserStateAsync(userId, cancellationToken);
        (page, pageSize) = NormalizePage(page, pageSize);
        var now = UtcNow;
        var isAdministrator = await IsAdministratorAsync(userId, cancellationToken);
        var query = includeCompleted
            ? _dbContext.Todos.AsNoTracking().Where(item => item.UserId == userId && item.Status != WorkspaceTodoStatus.Resolved)
            : _dbContext.Todos.AsNoTracking().Where(item => item.UserId == userId
                && item.Status == WorkspaceTodoStatus.Pending
                && (item.SnoozedUntilUtc == null || item.SnoozedUntilUtc <= now));
        var candidates = await query.OrderByDescending(item => item.UpdatedAtUtc).ThenByDescending(item => item.Id)
            .Take(500).ToArrayAsync(cancellationToken);
        var visible = new List<WorkspaceTodo>(candidates.Length);
        foreach (var item in candidates)
        {
            var canRead = item.RepositoryId is not long repositoryId
                || await _permissionQuery.CanReadRepositoryAsync(repositoryId, userId, isAdministrator, cancellationToken);
            if (canRead && item.TeamId is long teamId) canRead = await IsTeamMemberAsync(teamId, userId, cancellationToken);
            if (canRead)
            {
                visible.Add(MapTodo(item));
                continue;
            }
            if (item.Status == WorkspaceTodoStatus.Pending)
            {
                var stale = await _dbContext.Todos.SingleAsync(value => value.Id == item.Id, cancellationToken);
                stale.Status = WorkspaceTodoStatus.Resolved;
                stale.UpdatedAtUtc = now;
                stale.Version++;
            }
        }
        if (_dbContext.ChangeTracker.HasChanges()) await _dbContext.SaveChangesAsync(cancellationToken);
        return new WorkspacePage<WorkspaceTodo>(page, pageSize, visible.Count,
            visible.Skip((page - 1) * pageSize).Take(pageSize).ToArray());
    }

    public Task<bool> CompleteTodoAsync(long todoId, string userId, long version, CancellationToken cancellationToken = default) =>
        ChangeTodoAsync(todoId, userId, version, WorkspaceTodoStatus.Completed, null, cancellationToken);

    public Task<bool> RestoreTodoAsync(long todoId, string userId, long version, CancellationToken cancellationToken = default) =>
        ChangeTodoAsync(todoId, userId, version, WorkspaceTodoStatus.Pending, null, cancellationToken);

    public Task<bool> SnoozeTodoAsync(long todoId, string userId, long version, DateTime snoozedUntilUtc, CancellationToken cancellationToken = default)
    {
        if (snoozedUntilUtc.Kind != DateTimeKind.Utc || snoozedUntilUtc <= UtcNow || snoozedUntilUtc > UtcNow.AddDays(30))
        {
            return Task.FromResult(false);
        }
        return ChangeTodoAsync(todoId, userId, version, WorkspaceTodoStatus.Pending, snoozedUntilUtc, cancellationToken);
    }

    public async Task<WorkspacePage<WorkspaceNotification>> GetNotificationsAsync(string userId, bool isAdministrator, WorkspaceNotificationQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureUserStateAsync(userId, cancellationToken);
        var (page, pageSize) = NormalizePage(query.Page, query.PageSize);
        var source = _dbContext.Notifications.AsNoTracking().Where(item => item.UserId == userId);
        if (query.UnreadOnly) source = source.Where(item => item.ReadAtUtc == null);
        if (query.Reason is not null) source = source.Where(item => item.Reason == query.Reason);
        if (query.ResourceType is not null) source = source.Where(item => item.ResourceType == query.ResourceType);
        if (query.TeamOnly) source = source.Where(item => item.TeamId != null);
        var candidates = await (from item in source
            join repository in _dbContext.Repositories.AsNoTracking() on item.RepositoryId equals repository.Id into repositories
            from repository in repositories.DefaultIfEmpty()
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository!.NamespaceId equals repositoryNamespace.Id into namespaces
            from repositoryNamespace in namespaces.DefaultIfEmpty()
            join actor in _dbContext.Users.AsNoTracking() on item.ActorUserId equals actor.Id into actors
            from actor in actors.DefaultIfEmpty()
            join team in _dbContext.Teams.AsNoTracking() on item.TeamId equals team.Id into teams
            from team in teams.DefaultIfEmpty()
            orderby item.CreatedAtUtc descending, item.Id descending
            select new { Item = item, Repository = repository, Namespace = repositoryNamespace, Actor = actor, Team = team })
            .Take(500).ToArrayAsync(cancellationToken);
        var visible = new List<WorkspaceNotification>(candidates.Length);
        foreach (var row in candidates)
        {
            if (row.Item.RepositoryId is long repositoryId
                && !await _permissionQuery.CanReadRepositoryAsync(repositoryId, userId, isAdministrator, cancellationToken)) continue;
            if (row.Item.TeamId is long teamId
                && !await IsTeamMemberAsync(teamId, userId, cancellationToken)) continue;
            visible.Add(new WorkspaceNotification(row.Item.Id, row.Item.ResourceType, row.Item.Reason, row.Item.Title,
                row.Item.Url, row.Actor?.UserName, row.Namespace?.Slug, row.Repository?.Name, row.Team?.Name,
                row.Item.CreatedAtUtc, row.Item.ReadAtUtc));
        }
        var total = visible.Count;
        return new WorkspacePage<WorkspaceNotification>(page, pageSize, total,
            visible.Skip((page - 1) * pageSize).Take(pageSize).ToArray());
    }

    public async Task<bool> MarkNotificationReadAsync(long notificationId, string userId, CancellationToken cancellationToken = default)
    {
        var item = await _dbContext.Notifications.SingleOrDefaultAsync(value => value.Id == notificationId && value.UserId == userId, cancellationToken);
        if (item is null) return false;
        var isAdministrator = await IsAdministratorAsync(userId, cancellationToken);
        if (item.RepositoryId is long repositoryId
            && !await _permissionQuery.CanReadRepositoryAsync(repositoryId, userId, isAdministrator, cancellationToken)) return false;
        if (item.TeamId is long teamId && !await IsTeamMemberAsync(teamId, userId, cancellationToken)) return false;
        item.ReadAtUtc ??= UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> MarkAllNotificationsReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Notifications.Where(item => item.UserId == userId && item.ReadAtUtc == null).ToArrayAsync(cancellationToken);
        var now = UtcNow;
        var isAdministrator = await IsAdministratorAsync(userId, cancellationToken);
        foreach (var item in items)
        {
            if (item.RepositoryId is long repositoryId
                && !await _permissionQuery.CanReadRepositoryAsync(repositoryId, userId, isAdministrator, cancellationToken)) continue;
            if (item.TeamId is long teamId && !await IsTeamMemberAsync(teamId, userId, cancellationToken)) continue;
            item.ReadAtUtc = now;
        }
        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkspacePage<WorkspaceActivity>> GetFeedAsync(string userId, bool isAdministrator, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var repositoryIds = await GetContextRepositoryIdsAsync(userId, isAdministrator, cancellationToken);
        var teamIds = await _dbContext.UserTeamRoles.AsNoTracking().Where(item => item.UserId == userId)
            .Select(item => item.TeamId).ToArrayAsync(cancellationToken);
        var source = _dbContext.ActivityEvents.AsNoTracking().Where(item =>
            (item.RepositoryId != null && repositoryIds.Contains(item.RepositoryId.Value))
            || (item.RepositoryId == null && item.TeamId != null && teamIds.Contains(item.TeamId.Value)));
        var total = await source.CountAsync(cancellationToken);
        var rows = await (from item in source
            join repository in _dbContext.Repositories.AsNoTracking() on item.RepositoryId equals repository.Id into repositories
            from repository in repositories.DefaultIfEmpty()
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository!.NamespaceId equals repositoryNamespace.Id into namespaces
            from repositoryNamespace in namespaces.DefaultIfEmpty()
            join actor in _dbContext.Users.AsNoTracking() on item.ActorUserId equals actor.Id into actors
            from actor in actors.DefaultIfEmpty()
            join team in _dbContext.Teams.AsNoTracking() on item.TeamId equals team.Id into teams
            from team in teams.DefaultIfEmpty()
            orderby item.OccurredAtUtc descending, item.EventId descending
            select new WorkspaceActivity(item.EventId, item.SchemaVersion, item.Type, item.Title, item.Url,
                actor == null ? null : actor.UserName, repositoryNamespace == null ? null : repositoryNamespace.Slug,
                repository == null ? null : repository.Name, team == null ? null : team.Name, item.OccurredAtUtc))
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return new WorkspacePage<WorkspaceActivity>(page, pageSize, total, rows);
    }

    public async Task<IReadOnlyList<WorkspaceRepository>> GetRepositoriesAsync(string userId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var repositoryIds = await GetContextRepositoryIdsAsync(userId, isAdministrator, cancellationToken);
        var rows = await LoadRepositoriesAsync(_dbContext.Repositories.AsNoTracking().Where(item => repositoryIds.Contains(item.Id)), userId, cancellationToken);
        return rows.OrderBy(item => item.NamespaceSlug, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RepositorySlug, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<WorkspaceTeam>> GetTeamsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await (from role in _dbContext.UserTeamRoles.AsNoTracking()
            join team in _dbContext.Teams.AsNoTracking() on role.TeamId equals team.Id
            join teamNamespace in _dbContext.Namespaces.AsNoTracking() on team.Id equals teamNamespace.TeamId
            where role.UserId == userId
            orderby team.NormalizedName
            select new WorkspaceTeam(team.Id, teamNamespace.Slug, team.DisplayName, team.Description, role.IsAdministrator))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<PublicProfile?> GetPublicProfileAsync(string userName, PublicProfileTab tab, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var normalized = userName.Trim().ToUpperInvariant();
        var user = await _dbContext.Users.AsNoTracking().SingleOrDefaultAsync(item => item.NormalizedUserName == normalized, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.UserName)) return null;
        IQueryable<GitCandyRepository> repositories = tab == PublicProfileTab.Stars
            ? from star in _dbContext.RepositoryStars.AsNoTracking()
              join repository in _dbContext.Repositories.AsNoTracking() on star.RepositoryId equals repository.Id
              where star.UserId == user.Id && !repository.IsPrivate && repository.AllowAnonymousRead
              select repository
            : from repository in _dbContext.Repositories.AsNoTracking()
              join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository.NamespaceId equals repositoryNamespace.Id
              where repositoryNamespace.UserId == user.Id && !repository.IsPrivate && repository.AllowAnonymousRead
              select repository;
        if (tab is PublicProfileTab.Packages or PublicProfileTab.Teams) repositories = repositories.Where(static _ => false);
        var total = await repositories.CountAsync(cancellationToken);
        var pageRows = repositories.OrderBy(item => item.NormalizedName).Skip((page - 1) * pageSize).Take(pageSize);
        var items = await LoadRepositoriesAsync(pageRows, null, cancellationToken);
        var teams = tab == PublicProfileTab.Teams
            ? await GetPublicTeamsAsync(user.Id, cancellationToken)
            : [];
        return new PublicProfile(user.UserName, user.DisplayName ?? user.UserName, user.Description ?? string.Empty, tab,
            new WorkspacePage<WorkspaceRepository>(page, pageSize, total, items), teams, 0);
    }

    public async Task<bool> SetStarAsync(long repositoryId, string userId, bool starred, CancellationToken cancellationToken = default)
    {
        var isAdministrator = await IsAdministratorAsync(userId, cancellationToken);
        if (!await _permissionQuery.CanReadRepositoryAsync(repositoryId, userId, isAdministrator, cancellationToken)) return false;
        var existing = await _dbContext.RepositoryStars.SingleOrDefaultAsync(item => item.RepositoryId == repositoryId && item.UserId == userId, cancellationToken);
        if (starred && existing is null)
            _dbContext.RepositoryStars.Add(new GitCandyRepositoryStar { RepositoryId = repositoryId, UserId = userId, CreatedAtUtc = UtcNow });
        else if (!starred && existing is not null)
            _dbContext.RepositoryStars.Remove(existing);
        else
            return true;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            return starred == await _dbContext.RepositoryStars.AsNoTracking()
                .AnyAsync(item => item.RepositoryId == repositoryId && item.UserId == userId, cancellationToken);
        }
    }

    public Task<bool> IsStarredAsync(long repositoryId, string userId, CancellationToken cancellationToken = default) =>
        _dbContext.RepositoryStars.AsNoTracking().AnyAsync(
            item => item.RepositoryId == repositoryId && item.UserId == userId,
            cancellationToken);

    public async Task<ExplorePage> ExploreAsync(ExploreQuery query, CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = NormalizePage(query.Page, query.PageSize);
        var latest = await _dbContext.RepositoryRecommendationSnapshots.AsNoTracking()
            .OrderByDescending(item => item.CalculatedAtUtc).ThenByDescending(item => item.SnapshotId)
            .Select(item => new { item.SnapshotId, item.AlgorithmVersion, item.CalculatedAtUtc }).FirstOrDefaultAsync(cancellationToken);
        IQueryable<GitCandyRepository> publicRepositories = _dbContext.Repositories.AsNoTracking()
            .Where(item => !item.IsPrivate && item.AllowAnonymousRead);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpperInvariant();
            publicRepositories = publicRepositories.Where(item => item.NormalizedName.Contains(search) || item.Description.ToUpper().Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(query.License))
        {
            var license = query.License.Trim().ToUpperInvariant();
            publicRepositories = publicRepositories.Where(repository => _dbContext.RepositoryMetricsDaily
                .Any(metric => metric.RepositoryId == repository.Id && metric.LicenseSpdx != null && metric.LicenseSpdx.ToUpper() == license));
        }
        var total = await publicRepositories.CountAsync(cancellationToken);
        IQueryable<GitCandyRepository> ordered;
        if (latest is null)
        {
            ordered = publicRepositories.OrderByDescending(item => item.CreatedAtUtc).ThenBy(item => item.NormalizedName);
        }
        else
        {
            ordered = from repository in publicRepositories
                join snapshot in _dbContext.RepositoryRecommendationSnapshots.AsNoTracking().Where(item => item.SnapshotId == latest.SnapshotId)
                    on repository.Id equals snapshot.RepositoryId into snapshots
                from snapshot in snapshots.DefaultIfEmpty()
                orderby snapshot == null ? int.MaxValue : snapshot.Rank, repository.NormalizedName
                select repository;
        }
        var rows = await LoadRepositoriesAsync(ordered.Skip((page - 1) * pageSize).Take(pageSize), null, cancellationToken, latest?.SnapshotId);
        if (latest is null)
        {
            rows = rows.Select(item => item with { Reason = "Recently active public repository" }).ToArray();
        }
        return new ExplorePage(new WorkspacePage<WorkspaceRepository>(page, pageSize, total, rows),
            latest?.AlgorithmVersion ?? RecommendationVersion, latest?.CalculatedAtUtc, latest is null);
    }

    public async Task RefreshProjectionsAsync(CancellationToken cancellationToken = default)
    {
        await ProjectIssueActivitiesAsync(cancellationToken);
        await ProjectPullRequestActivitiesAsync(cancellationToken);
        await RefreshMetricsAndRecommendationsAsync(cancellationToken);
        await _dbContext.ActivityEvents.Where(item => item.RetainUntilUtc < UtcNow).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.RepositoryPageViews.Where(item => item.DayUtc < UtcNow.Date.AddDays(-2)).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task SynchronizeUserStateAsync(string userId, CancellationToken cancellationToken)
    {
        var activeKeys = new HashSet<string>(StringComparer.Ordinal);
        var issues = await (from issue in _dbContext.Issues.AsNoTracking()
            join repository in _dbContext.Repositories.AsNoTracking() on issue.RepositoryId equals repository.Id
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository.NamespaceId equals repositoryNamespace.Id
            where issue.AssigneeUserId == userId && issue.State == IssueState.Open
            select new { Issue = issue, Repository = repository, Namespace = repositoryNamespace }).ToArrayAsync(cancellationToken);
        foreach (var row in issues)
        {
            if (!await _permissionQuery.CanReadRepositoryAsync(row.Repository.Id, userId, false, cancellationToken)) continue;
            var resource = $"issue:{row.Issue.Id}";
            activeKeys.Add(TodoKey(WorkspaceTodoKind.IssueAssignment, resource));
            await UpsertTodoAsync(userId, row.Repository.Id, null, WorkspaceResourceType.Issue, resource,
                WorkspaceTodoKind.IssueAssignment, $"Assigned: {row.Issue.Title}",
                $"/{row.Namespace.Slug}/{row.Repository.Name}/issues/{row.Issue.Number}", row.Issue.UpdatedAtUtc, cancellationToken);
        }
        var reviewRequests = await (from reviewer in _dbContext.PullRequestReviewers.AsNoTracking()
            join pullRequest in _dbContext.PullRequests.AsNoTracking() on reviewer.PullRequestId equals pullRequest.Id
            join repository in _dbContext.Repositories.AsNoTracking() on pullRequest.RepositoryId equals repository.Id
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository.NamespaceId equals repositoryNamespace.Id
            where reviewer.ReviewerUserId == userId && pullRequest.State == PullRequestState.Open
            select new { Reviewer = reviewer, PullRequest = pullRequest, Repository = repository, Namespace = repositoryNamespace }).ToArrayAsync(cancellationToken);
        foreach (var row in reviewRequests)
        {
            if (!await _permissionQuery.CanReadRepositoryAsync(row.Repository.Id, userId, false, cancellationToken)) continue;
            var reviewed = await _dbContext.PullRequestReviews.AsNoTracking().AnyAsync(item => item.PullRequestId == row.PullRequest.Id
                && item.ReviewerUserId == userId && item.ReviewerRequestVersion == row.Reviewer.Version
                && item.State != PullRequestReviewState.Dismissed, cancellationToken);
            if (reviewed) continue;
            var resource = $"pull-request:{row.PullRequest.Id}";
            activeKeys.Add(TodoKey(WorkspaceTodoKind.PullRequestReview, resource));
            await UpsertTodoAsync(userId, row.Repository.Id, null, WorkspaceResourceType.PullRequest, resource,
                WorkspaceTodoKind.PullRequestReview, $"Review requested: {row.PullRequest.Title}",
                $"/{row.Namespace.Slug}/{row.Repository.Name}/pulls/{row.PullRequest.Number}", row.Reviewer.RequestedAtUtc, cancellationToken);
            var reviewTeamId = await ResolveTeamSourceAsync(row.Repository.Id, userId, cancellationToken);
            await UpsertNotificationAsync($"pull-request-review:{row.PullRequest.Id}:{row.Reviewer.Version}", userId,
                row.Reviewer.RequestedByUserId, row.Repository.Id, reviewTeamId, WorkspaceResourceType.PullRequest, resource,
                WorkspaceNotificationReason.ReviewRequest, $"Review requested: {row.PullRequest.Title}",
                $"/{row.Namespace.Slug}/{row.Repository.Name}/pulls/{row.PullRequest.Number}", row.Reviewer.RequestedAtUtc, null, cancellationToken);
        }
        var mentions = await (from notification in _dbContext.IssueNotifications.AsNoTracking()
            join issue in _dbContext.Issues.AsNoTracking() on notification.IssueId equals issue.Id
            join repository in _dbContext.Repositories.AsNoTracking() on notification.RepositoryId equals repository.Id
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository.NamespaceId equals repositoryNamespace.Id
            where notification.UserId == userId
            select new { Notification = notification, Issue = issue, Repository = repository, Namespace = repositoryNamespace })
            .Take(500).ToArrayAsync(cancellationToken);
        foreach (var row in mentions)
        {
            var canRead = await _permissionQuery.CanReadRepositoryAsync(row.Repository.Id, userId, false, cancellationToken);
            var resource = $"issue:{row.Issue.Id}";
            if (canRead && row.Notification.Type == IssueNotificationType.Mention && row.Issue.State == IssueState.Open)
            {
                activeKeys.Add(TodoKey(WorkspaceTodoKind.Mention, resource));
                await UpsertTodoAsync(userId, row.Repository.Id, null, WorkspaceResourceType.Issue, resource,
                    WorkspaceTodoKind.Mention, $"Mentioned: {row.Issue.Title}",
                    $"/{row.Namespace.Slug}/{row.Repository.Name}/issues/{row.Issue.Number}", row.Notification.CreatedAtUtc, cancellationToken);
            }
            var notificationTeamId = canRead ? await ResolveTeamSourceAsync(row.Repository.Id, userId, cancellationToken) : null;
            await UpsertNotificationAsync($"issue-notification:{row.Notification.Id}", userId, row.Notification.ActorUserId,
                row.Repository.Id, notificationTeamId, WorkspaceResourceType.Issue, resource, MapReason(row.Notification.Type),
                row.Issue.Title, $"/{row.Namespace.Slug}/{row.Repository.Name}/issues/{row.Issue.Number}",
                row.Notification.CreatedAtUtc, row.Notification.ReadAtUtc, cancellationToken);
        }
        var blocked = await (from pullRequest in _dbContext.PullRequests.AsNoTracking()
            join repository in _dbContext.Repositories.AsNoTracking() on pullRequest.RepositoryId equals repository.Id
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository.NamespaceId equals repositoryNamespace.Id
            where pullRequest.AuthorUserId == userId && pullRequest.State == PullRequestState.Open
                && _dbContext.PullRequestReviews.Any(review => review.PullRequestId == pullRequest.Id
                    && review.State == PullRequestReviewState.ChangesRequested && review.DismissedAtUtc == null)
            select new { PullRequest = pullRequest, Repository = repository, Namespace = repositoryNamespace }).ToArrayAsync(cancellationToken);
        foreach (var row in blocked)
        {
            if (!await _permissionQuery.CanReadRepositoryAsync(row.Repository.Id, userId, false, cancellationToken)) continue;
            var resource = $"pull-request:{row.PullRequest.Id}";
            activeKeys.Add(TodoKey(WorkspaceTodoKind.BlockedPullRequest, resource));
            await UpsertTodoAsync(userId, row.Repository.Id, null, WorkspaceResourceType.PullRequest, resource,
                WorkspaceTodoKind.BlockedPullRequest, $"Changes requested: {row.PullRequest.Title}",
                $"/{row.Namespace.Slug}/{row.Repository.Name}/pulls/{row.PullRequest.Number}", row.PullRequest.UpdatedAtUtc, cancellationToken);
        }
        var activeTodos = await _dbContext.Todos.Where(item => item.UserId == userId && item.Status != WorkspaceTodoStatus.Resolved).ToArrayAsync(cancellationToken);
        foreach (var todo in activeTodos)
        {
            if (todo.Status == WorkspaceTodoStatus.Pending && !activeKeys.Contains(TodoKey(todo.Kind, todo.ResourceId)))
            {
                todo.Status = WorkspaceTodoStatus.Resolved;
                todo.UpdatedAtUtc = UtcNow;
                todo.Version++;
            }
        }
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogDebug(exception, "A concurrent workspace projection already created the same user state.");
            _dbContext.ChangeTracker.Clear();
        }
    }

    private async Task EnsureUserStateAsync(string userId, CancellationToken cancellationToken)
    {
        if (!_synchronizedUsers.Add(userId)) return;
        try { await SynchronizeUserStateAsync(userId, cancellationToken); }
        catch
        {
            _synchronizedUsers.Remove(userId);
            throw;
        }
    }

    private async Task<WorkspaceModule<T>> LoadModuleAsync<T>(string name, Func<Task<T>> load, T fallback)
    {
        try { return new WorkspaceModule<T>(await load()); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Workspace module {WorkspaceModule} is unavailable.", name);
            return new WorkspaceModule<T>(fallback, false, "Temporarily unavailable");
        }
    }

    private async Task<T> LoadValueAsync<T>(string name, Func<Task<T>> load, T fallback)
    {
        try { return await load(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Workspace value {WorkspaceValue} is unavailable.", name);
            return fallback;
        }
    }

    private async Task UpsertTodoAsync(string userId, long? repositoryId, long? teamId, WorkspaceResourceType resourceType,
        string resourceId, WorkspaceTodoKind kind, string title, string url, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        var item = await _dbContext.Todos.SingleOrDefaultAsync(value => value.UserId == userId && value.Kind == kind
            && value.ResourceType == resourceType && value.ResourceId == resourceId, cancellationToken);
        if (item is null)
        {
            _dbContext.Todos.Add(new GitCandyTodo { UserId = userId, RepositoryId = repositoryId, TeamId = teamId,
                ResourceType = resourceType, ResourceId = resourceId, Kind = kind, Status = WorkspaceTodoStatus.Pending,
                Title = title, Url = url, CreatedAtUtc = occurredAtUtc, UpdatedAtUtc = occurredAtUtc });
        }
        else if (item.Status == WorkspaceTodoStatus.Resolved)
        {
            item.Status = WorkspaceTodoStatus.Pending;
            item.CompletedAtUtc = null;
            item.SnoozedUntilUtc = null;
            item.UpdatedAtUtc = occurredAtUtc;
            item.Version++;
        }
    }

    private async Task UpsertNotificationAsync(string eventId, string userId, string? actorUserId, long? repositoryId,
        long? teamId, WorkspaceResourceType resourceType, string resourceId, WorkspaceNotificationReason reason,
        string title, string url, DateTime createdAtUtc, DateTime? readAtUtc, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Notifications.AnyAsync(item => item.UserId == userId && item.EventId == eventId, cancellationToken);
        if (!exists) _dbContext.Notifications.Add(new GitCandyNotification { EventId = eventId, UserId = userId,
            ActorUserId = actorUserId, RepositoryId = repositoryId, TeamId = teamId, ResourceType = resourceType,
            ResourceId = resourceId, Reason = reason, Title = title, Url = url, CreatedAtUtc = createdAtUtc, ReadAtUtc = readAtUtc });
    }

    private async Task<bool> ChangeTodoAsync(long todoId, string userId, long version, WorkspaceTodoStatus status,
        DateTime? snoozedUntilUtc, CancellationToken cancellationToken)
    {
        var item = await _dbContext.Todos.SingleOrDefaultAsync(value => value.Id == todoId && value.UserId == userId, cancellationToken);
        if (item is null || item.Version != version || item.Status == WorkspaceTodoStatus.Resolved) return false;
        var isAdministrator = await IsAdministratorAsync(userId, cancellationToken);
        if (item.RepositoryId is long repositoryId
            && !await _permissionQuery.CanReadRepositoryAsync(repositoryId, userId, isAdministrator, cancellationToken)) return false;
        if (item.TeamId is long teamId && !await IsTeamMemberAsync(teamId, userId, cancellationToken)) return false;
        item.Status = status;
        item.SnoozedUntilUtc = snoozedUntilUtc;
        item.CompletedAtUtc = status == WorkspaceTodoStatus.Completed ? UtcNow : null;
        item.UpdatedAtUtc = UtcNow;
        item.Version++;
        try { await _dbContext.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    private async Task ProjectIssueActivitiesAsync(CancellationToken cancellationToken)
    {
        var rows = await (from timeline in _dbContext.IssueTimelineEvents.AsNoTracking()
            join issue in _dbContext.Issues.AsNoTracking() on timeline.IssueId equals issue.Id
            join repository in _dbContext.Repositories.AsNoTracking() on issue.RepositoryId equals repository.Id
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository.NamespaceId equals repositoryNamespace.Id
            orderby timeline.Id descending
            select new { Timeline = timeline, Issue = issue, Repository = repository, Namespace = repositoryNamespace }).Take(2000).ToArrayAsync(cancellationToken);
        var ids = rows.Select(item => $"issue-timeline:{item.Timeline.Id}").ToArray();
        var existing = await _dbContext.ActivityEvents.AsNoTracking().Where(item => ids.Contains(item.EventId)).Select(item => item.EventId).ToArrayAsync(cancellationToken);
        var known = existing.ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var eventId = $"issue-timeline:{row.Timeline.Id}";
            if (known.Contains(eventId)) continue;
            _dbContext.ActivityEvents.Add(new GitCandyActivityEvent { EventId = eventId, ActorUserId = row.Timeline.ActorUserId,
                RepositoryId = row.Repository.Id, ResourceType = WorkspaceResourceType.Issue, ResourceId = $"issue:{row.Issue.Id}",
                Type = row.Timeline.Type == IssueEventType.Created ? WorkspaceActivityType.IssueCreated : WorkspaceActivityType.IssueUpdated,
                Title = row.Issue.Title, Url = $"/{row.Namespace.Slug}/{row.Repository.Name}/issues/{row.Issue.Number}",
                OccurredAtUtc = row.Timeline.CreatedAtUtc, RetainUntilUtc = row.Timeline.CreatedAtUtc.AddDays(180) });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProjectPullRequestActivitiesAsync(CancellationToken cancellationToken)
    {
        var rows = await (from timeline in _dbContext.PullRequestTimelineEvents.AsNoTracking()
            join pullRequest in _dbContext.PullRequests.AsNoTracking() on timeline.PullRequestId equals pullRequest.Id
            join repository in _dbContext.Repositories.AsNoTracking() on pullRequest.RepositoryId equals repository.Id
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository.NamespaceId equals repositoryNamespace.Id
            orderby timeline.Id descending
            select new { Timeline = timeline, PullRequest = pullRequest, Repository = repository, Namespace = repositoryNamespace }).Take(2000).ToArrayAsync(cancellationToken);
        var ids = rows.Select(item => $"pull-request-timeline:{item.Timeline.Id}").ToArray();
        var existing = await _dbContext.ActivityEvents.AsNoTracking().Where(item => ids.Contains(item.EventId)).Select(item => item.EventId).ToArrayAsync(cancellationToken);
        var known = existing.ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var eventId = $"pull-request-timeline:{row.Timeline.Id}";
            if (known.Contains(eventId)) continue;
            _dbContext.ActivityEvents.Add(new GitCandyActivityEvent { EventId = eventId, ActorUserId = row.Timeline.ActorUserId,
                RepositoryId = row.Repository.Id, ResourceType = WorkspaceResourceType.PullRequest, ResourceId = $"pull-request:{row.PullRequest.Id}",
                Type = row.Timeline.Type == PullRequestEventType.Created ? WorkspaceActivityType.PullRequestCreated
                    : row.Timeline.Type == PullRequestEventType.ReviewSubmitted ? WorkspaceActivityType.ReviewSubmitted : WorkspaceActivityType.PullRequestUpdated,
                Title = row.PullRequest.Title, Url = $"/{row.Namespace.Slug}/{row.Repository.Name}/pulls/{row.PullRequest.Number}",
                OccurredAtUtc = row.Timeline.CreatedAtUtc, RetainUntilUtc = row.Timeline.CreatedAtUtc.AddDays(180) });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task RefreshMetricsAndRecommendationsAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow;
        var nowOffset = new DateTimeOffset(now, TimeSpan.Zero);
        var day = now.Date;
        var publicRepositories = await _dbContext.Repositories.AsNoTracking().Where(item => !item.IsPrivate && item.AllowAnonymousRead)
            .OrderBy(item => item.Id).ToArrayAsync(cancellationToken);
        var starRows = await (from star in _dbContext.RepositoryStars.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking() on star.UserId equals user.Id
            join repository in _dbContext.Repositories.AsNoTracking() on star.RepositoryId equals repository.Id
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository.NamespaceId equals repositoryNamespace.Id
            where !repository.IsPrivate && repository.AllowAnonymousRead
            select new { Star = star, user.LockoutEnd, NamespaceOwnerUserId = repositoryNamespace.UserId }).ToArrayAsync(cancellationToken);
        var suspiciousUsers = starRows.Where(item => item.Star.CreatedAtUtc >= day)
            .GroupBy(item => item.Star.UserId, StringComparer.Ordinal)
            .Where(group => group.Count() > 50)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var directOwnerPairs = (await _dbContext.UserRepositoryRoles.AsNoTracking().Where(item => item.IsOwner)
            .Select(item => new { item.RepositoryId, item.UserId }).ToArrayAsync(cancellationToken))
            .Select(item => $"{item.RepositoryId}:{item.UserId}").ToHashSet(StringComparer.Ordinal);
        foreach (var repository in publicRepositories)
        {
            var validStars = starRows.Where(item => item.Star.RepositoryId == repository.Id
                && !string.Equals(item.Star.UserId, item.NamespaceOwnerUserId, StringComparison.Ordinal)
                && !directOwnerPairs.Contains($"{repository.Id}:{item.Star.UserId}")
                && !suspiciousUsers.Contains(item.Star.UserId)
                && (item.LockoutEnd is null || item.LockoutEnd <= nowOffset)).ToArray();
            var starCount = validStars.Length;
            var todayGrowth = validStars.Count(item => item.Star.CreatedAtUtc >= day);
            var metric = await _dbContext.RepositoryMetricsDaily.SingleOrDefaultAsync(item => item.RepositoryId == repository.Id && item.DayUtc == day, cancellationToken);
            if (metric is null) _dbContext.RepositoryMetricsDaily.Add(new GitCandyRepositoryMetricDaily { RepositoryId = repository.Id,
                DayUtc = day, StarCount = starCount, StarNetGrowth = todayGrowth });
            else { metric.StarCount = starCount; metric.StarNetGrowth = todayGrowth; }
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        var metrics = await _dbContext.RepositoryMetricsDaily.AsNoTracking().Where(item => item.DayUtc >= day.AddDays(-90)).ToArrayAsync(cancellationToken);
        var aggregates = publicRepositories.Select(repository => new
        {
            Repository = repository,
            Commits = metrics.Where(item => item.RepositoryId == repository.Id).Sum(item => item.CommitCount),
            Stars = metrics.Where(item => item.RepositoryId == repository.Id).OrderByDescending(item => item.DayUtc).Select(item => item.StarCount).FirstOrDefault(),
            Downloads = metrics.Where(item => item.RepositoryId == repository.Id).Sum(item => item.SuccessfulDownloadCount + item.SuccessfulGitFetchCount),
            Views = metrics.Where(item => item.RepositoryId == repository.Id).Sum(item => item.UniquePageViewCount)
        }).ToArray();
        var maxCommits = Math.Max(1, aggregates.Select(item => item.Commits).DefaultIfEmpty().Max());
        var maxStars = Math.Max(1, aggregates.Select(item => item.Stars).DefaultIfEmpty().Max());
        var maxDownloads = Math.Max(1L, aggregates.Select(item => item.Downloads).DefaultIfEmpty().Max());
        var maxViews = Math.Max(1L, aggregates.Select(item => item.Views).DefaultIfEmpty().Max());
        var ranked = aggregates.Select(item => new
        {
            item.Repository,
            Commit = (double)item.Commits / maxCommits,
            Star = (double)item.Stars / maxStars,
            Download = (double)item.Downloads / maxDownloads,
            View = (double)item.Views / maxViews
        }).Select(item => new { item.Repository, item.Commit, item.Star, item.Download, item.View,
            Total = item.Commit * 0.4 + item.Star * 0.3 + item.Download * 0.2 + item.View * 0.1 })
          .OrderByDescending(item => item.Total).ThenByDescending(item => item.Repository.CreatedAtUtc).ThenBy(item => item.Repository.NormalizedName).ToArray();
        var snapshotId = Guid.NewGuid().ToString("N");
        for (var index = 0; index < ranked.Length; index++)
        {
            var item = ranked[index];
            var reason = item.Total == 0 ? "Recently active public repository" : GetRecommendationReason(item.Commit, item.Star, item.Download, item.View);
            _dbContext.RepositoryRecommendationSnapshots.Add(new GitCandyRepositoryRecommendationSnapshot { SnapshotId = snapshotId,
                RepositoryId = item.Repository.Id, AlgorithmVersion = RecommendationVersion, CalculatedAtUtc = now,
                CommitScore = item.Commit, StarScore = item.Star, DownloadScore = item.Download, PageViewScore = item.View,
                TotalScore = item.Total, Rank = index + 1, Explanation = reason });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        var retained = await _dbContext.RepositoryRecommendationSnapshots.AsNoTracking().Select(item => new { item.SnapshotId, item.CalculatedAtUtc })
            .Distinct().OrderByDescending(item => item.CalculatedAtUtc).Skip(5).Select(item => item.SnapshotId).ToArrayAsync(cancellationToken);
        if (retained.Length > 0) await _dbContext.RepositoryRecommendationSnapshots.Where(item => retained.Contains(item.SnapshotId)).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<WorkspaceRepository>> GetAttentionRepositoriesAsync(string userId, bool isAdministrator, int limit, CancellationToken cancellationToken)
    {
        var ids = await GetContextRepositoryIdsAsync(userId, isAdministrator, cancellationToken);
        var rows = await LoadRepositoriesAsync(_dbContext.Repositories.AsNoTracking().Where(item => ids.Contains(item.Id)), userId, cancellationToken);
        var pending = await _dbContext.Todos.AsNoTracking().Where(item => item.UserId == userId && item.Status == WorkspaceTodoStatus.Pending && item.RepositoryId != null)
            .GroupBy(item => item.RepositoryId!.Value).Select(group => new { RepositoryId = group.Key, Count = group.Count() }).ToDictionaryAsync(item => item.RepositoryId, item => item.Count, cancellationToken);
        var unread = await _dbContext.Notifications.AsNoTracking().Where(item => item.UserId == userId && item.ReadAtUtc == null && item.RepositoryId != null)
            .GroupBy(item => item.RepositoryId!.Value).Select(group => new { RepositoryId = group.Key, Count = group.Count() }).ToDictionaryAsync(item => item.RepositoryId, item => item.Count, cancellationToken);
        var interactions = await _dbContext.RepositoryInteractions.AsNoTracking().Where(item => item.UserId == userId)
            .ToDictionaryAsync(item => item.RepositoryId, item => item.LastInteractedAtUtc, cancellationToken);
        return rows.Select(item => item with { Reason = pending.TryGetValue(item.Id, out var todoCount) ? $"{todoCount} pending todo(s)"
                : unread.TryGetValue(item.Id, out var unreadCount) ? $"{unreadCount} unread notification(s)"
                : interactions.ContainsKey(item.Id) ? "Recent interaction" : "Repository access",
            UpdatedAtUtc = interactions.GetValueOrDefault(item.Id, item.UpdatedAtUtc) })
            .OrderByDescending(item => pending.ContainsKey(item.Id)).ThenByDescending(item => unread.ContainsKey(item.Id))
            .ThenByDescending(item => item.UpdatedAtUtc).ThenBy(item => item.NamespaceSlug, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RepositorySlug, StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
    }

    private async Task<long[]> GetContextRepositoryIdsAsync(string userId, bool isAdministrator, CancellationToken cancellationToken)
    {
        if (isAdministrator) return await _dbContext.Repositories.AsNoTracking().Select(item => item.Id).ToArrayAsync(cancellationToken);
        var readable = _dbContext.Repositories.AsNoTracking().Where(repository =>
            (!repository.IsPrivate && repository.AllowAnonymousRead)
            || _dbContext.UserRepositoryRoles.Any(role => role.RepositoryId == repository.Id && role.UserId == userId && role.AllowRead)
            || _dbContext.TeamRepositoryRoles.Any(teamRole => teamRole.RepositoryId == repository.Id && teamRole.AllowRead
                && _dbContext.UserTeamRoles.Any(member => member.TeamId == teamRole.TeamId && member.UserId == userId)));
        return await readable.Where(repository =>
            _dbContext.UserRepositoryRoles.Any(role => role.RepositoryId == repository.Id && role.UserId == userId && role.AllowRead)
            || _dbContext.RepositoryStars.Any(star => star.RepositoryId == repository.Id && star.UserId == userId)
            || _dbContext.RepositoryInteractions.Any(interaction => interaction.RepositoryId == repository.Id && interaction.UserId == userId)
            || _dbContext.Issues.Any(issue => issue.RepositoryId == repository.Id
                && (issue.AuthorUserId == userId || issue.AssigneeUserId == userId))
            || _dbContext.IssueSubscriptions.Any(subscription => subscription.UserId == userId && subscription.IsSubscribed
                && _dbContext.Issues.Any(issue => issue.Id == subscription.IssueId && issue.RepositoryId == repository.Id))
            || _dbContext.PullRequests.Any(pullRequest => pullRequest.RepositoryId == repository.Id
                && (pullRequest.AuthorUserId == userId || pullRequest.AssigneeUserId == userId
                    || _dbContext.PullRequestReviewers.Any(reviewer => reviewer.PullRequestId == pullRequest.Id && reviewer.ReviewerUserId == userId)))
            || _dbContext.TeamRepositoryRoles.Any(teamRole => teamRole.RepositoryId == repository.Id && teamRole.AllowRead
                && _dbContext.UserTeamRoles.Any(member => member.TeamId == teamRole.TeamId && member.UserId == userId)))
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<WorkspaceRepository>> LoadRepositoriesAsync(IQueryable<GitCandyRepository> repositories,
        string? userId, CancellationToken cancellationToken, string? snapshotId = null)
    {
        var rows = await (from repository in repositories
            join repositoryNamespace in _dbContext.Namespaces.AsNoTracking() on repository.NamespaceId equals repositoryNamespace.Id
            select new { Repository = repository, Namespace = repositoryNamespace.Slug,
                StarCount = _dbContext.RepositoryStars.Count(star => star.RepositoryId == repository.Id),
                IsStarred = userId != null && _dbContext.RepositoryStars.Any(star => star.RepositoryId == repository.Id && star.UserId == userId) })
            .ToArrayAsync(cancellationToken);
        Dictionary<long, string> reasons = [];
        if (snapshotId is not null) reasons = await _dbContext.RepositoryRecommendationSnapshots.AsNoTracking()
            .Where(item => item.SnapshotId == snapshotId).ToDictionaryAsync(item => item.RepositoryId, item => item.Explanation, cancellationToken);
        return rows.Select(item => new WorkspaceRepository(item.Repository.Id, item.Namespace, item.Repository.Name,
            item.Repository.Description, item.Repository.IsPrivate, item.IsStarred, item.StarCount,
            reasons.GetValueOrDefault(item.Repository.Id, string.Empty), item.Repository.CreatedAtUtc)).ToArray();
    }

    private async Task<IReadOnlyList<WorkspaceTeam>> GetPublicTeamsAsync(string userId, CancellationToken cancellationToken) =>
        await (from role in _dbContext.UserTeamRoles.AsNoTracking()
            join team in _dbContext.Teams.AsNoTracking() on role.TeamId equals team.Id
            join teamNamespace in _dbContext.Namespaces.AsNoTracking() on team.Id equals teamNamespace.TeamId
            where role.UserId == userId && _dbContext.Repositories.Any(repository => repository.NamespaceId == teamNamespace.Id
                && !repository.IsPrivate && repository.AllowAnonymousRead)
            orderby team.NormalizedName
            select new WorkspaceTeam(team.Id, teamNamespace.Slug, team.DisplayName, team.Description, false)).ToArrayAsync(cancellationToken);

    private async Task<int> CountVisibleUnreadAsync(string userId, bool isAdministrator, CancellationToken cancellationToken)
    {
        var candidates = await _dbContext.Notifications.AsNoTracking().Where(item => item.UserId == userId && item.ReadAtUtc == null)
            .Select(item => new { item.RepositoryId, item.TeamId }).Take(500).ToArrayAsync(cancellationToken);
        var count = 0;
        foreach (var item in candidates)
        {
            if (item.RepositoryId is long repositoryId && !await _permissionQuery.CanReadRepositoryAsync(repositoryId, userId, isAdministrator, cancellationToken)) continue;
            if (item.TeamId is long teamId && !await IsTeamMemberAsync(teamId, userId, cancellationToken)) continue;
            count++;
        }
        return count;
    }

    private Task<bool> IsTeamMemberAsync(long teamId, string userId, CancellationToken cancellationToken) =>
        _dbContext.UserTeamRoles.AsNoTracking().AnyAsync(item => item.TeamId == teamId && item.UserId == userId, cancellationToken);

    private Task<long?> ResolveTeamSourceAsync(long repositoryId, string userId, CancellationToken cancellationToken) =>
        (from teamRole in _dbContext.TeamRepositoryRoles.AsNoTracking()
         join member in _dbContext.UserTeamRoles.AsNoTracking() on teamRole.TeamId equals member.TeamId
         where teamRole.RepositoryId == repositoryId && teamRole.AllowRead && member.UserId == userId
         orderby teamRole.TeamId
         select (long?)teamRole.TeamId).FirstOrDefaultAsync(cancellationToken);

    private Task<bool> IsAdministratorAsync(string userId, CancellationToken cancellationToken) =>
        (from userRole in _dbContext.UserRoles.AsNoTracking()
         join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
         where userRole.UserId == userId && role.NormalizedName == "ADMINISTRATOR"
         select userRole).AnyAsync(cancellationToken);

    private static WorkspaceTodo MapTodo(GitCandyTodo item) => new(item.Id, item.Kind, item.Status, item.Title, item.Url,
        null, null, item.CreatedAtUtc, item.SnoozedUntilUtc, item.Version);
    private static string TodoKey(WorkspaceTodoKind kind, string resourceId) => $"{kind}:{resourceId}";
    private static WorkspaceNotificationReason MapReason(IssueNotificationType type) => type switch
    {
        IssueNotificationType.Mention => WorkspaceNotificationReason.Mention,
        IssueNotificationType.Assignment => WorkspaceNotificationReason.Assignment,
        IssueNotificationType.Reply => WorkspaceNotificationReason.Participation,
        _ => WorkspaceNotificationReason.Subscription
    };
    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, MaximumPageSize));
    private static string GetRecommendationReason(double commit, double star, double download, double view)
    {
        var maximum = Math.Max(Math.Max(commit, star), Math.Max(download, view));
        if (maximum == commit) return "Active development";
        if (maximum == star) return "Growing stars";
        if (maximum == download) return "Frequently downloaded";
        return "Frequently viewed";
    }
}
