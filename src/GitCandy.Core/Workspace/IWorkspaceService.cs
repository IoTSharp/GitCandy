namespace GitCandy.Workspace;

/// <summary>个人工作台、公开个人页和仓库发现的应用服务边界。</summary>
public interface IWorkspaceService
{
    Task<WorkspaceDashboard> GetDashboardAsync(string userId, bool isAdministrator, CancellationToken cancellationToken = default);
    Task<WorkspacePage<WorkspaceTodo>> GetTodosAsync(string userId, int page, int pageSize, bool includeCompleted = false, CancellationToken cancellationToken = default);
    Task<bool> CompleteTodoAsync(long todoId, string userId, long version, CancellationToken cancellationToken = default);
    Task<bool> RestoreTodoAsync(long todoId, string userId, long version, CancellationToken cancellationToken = default);
    Task<bool> SnoozeTodoAsync(long todoId, string userId, long version, DateTime snoozedUntilUtc, CancellationToken cancellationToken = default);
    Task<WorkspacePage<WorkspaceNotification>> GetNotificationsAsync(string userId, bool isAdministrator, WorkspaceNotificationQuery query, CancellationToken cancellationToken = default);
    Task<bool> MarkNotificationReadAsync(long notificationId, string userId, CancellationToken cancellationToken = default);
    Task<int> MarkAllNotificationsReadAsync(string userId, CancellationToken cancellationToken = default);
    Task<WorkspacePage<WorkspaceActivity>> GetFeedAsync(string userId, bool isAdministrator, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkspaceRepository>> GetRepositoriesAsync(string userId, bool isAdministrator, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkspaceTeam>> GetTeamsAsync(string userId, CancellationToken cancellationToken = default);
    Task<PublicProfile?> GetPublicProfileAsync(string userName, PublicProfileTab tab, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<bool> SetStarAsync(long repositoryId, string userId, bool starred, CancellationToken cancellationToken = default);
    Task<bool> IsStarredAsync(long repositoryId, string userId, CancellationToken cancellationToken = default);
    Task<ExplorePage> ExploreAsync(ExploreQuery query, CancellationToken cancellationToken = default);
    Task RefreshProjectionsAsync(CancellationToken cancellationToken = default);
}
