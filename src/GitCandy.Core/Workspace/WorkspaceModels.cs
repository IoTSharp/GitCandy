namespace GitCandy.Workspace;

/// <summary>私人工作台 Todo 的触发原因。</summary>
public enum WorkspaceTodoKind
{
    IssueAssignment,
    PullRequestReview,
    Mention,
    BlockedPullRequest,
    RepositoryRequest,
    TeamRequest
}

/// <summary>Todo 独立于通知已读状态的工作状态。</summary>
public enum WorkspaceTodoStatus
{
    Pending,
    Completed,
    Resolved
}

/// <summary>统一通知的业务资源类型。</summary>
public enum WorkspaceResourceType
{
    Issue,
    PullRequest,
    Repository,
    Team,
    Release,
    Package
}

/// <summary>统一通知的投递原因。</summary>
public enum WorkspaceNotificationReason
{
    Assignment,
    ReviewRequest,
    Mention,
    Participation,
    Subscription,
    Team
}

/// <summary>统一通知及外部投递使用的稳定事件类型。</summary>
public enum WorkspaceNotificationEventType
{
    Issue,
    PullRequest,
    Review,
    Check,
    Release
}

/// <summary>版本化活动事件类型。</summary>
public enum WorkspaceActivityType
{
    IssueCreated,
    IssueUpdated,
    PullRequestCreated,
    PullRequestUpdated,
    ReviewSubmitted,
    Push,
    Release
}

/// <summary>私人 Todo 摘要。</summary>
public sealed record WorkspaceTodo(
    long Id,
    WorkspaceTodoKind Kind,
    WorkspaceTodoStatus Status,
    string Title,
    string Url,
    string? NamespaceSlug,
    string? RepositorySlug,
    DateTime CreatedAtUtc,
    DateTime? SnoozedUntilUtc,
    long Version);

/// <summary>统一通知摘要。</summary>
public sealed record WorkspaceNotification(
    long Id,
    WorkspaceNotificationEventType EventType,
    WorkspaceResourceType ResourceType,
    WorkspaceNotificationReason Reason,
    string Title,
    string Url,
    string? Actor,
    string? NamespaceSlug,
    string? RepositorySlug,
    string? Team,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

/// <summary>Feed 活动摘要。</summary>
public sealed record WorkspaceActivity(
    string EventId,
    int SchemaVersion,
    WorkspaceActivityType Type,
    string Title,
    string Url,
    string? Actor,
    string? NamespaceSlug,
    string? RepositorySlug,
    string? Team,
    DateTime OccurredAtUtc);

/// <summary>工作台和发现页使用的仓库投影。</summary>
public sealed record WorkspaceRepository(
    long Id,
    string NamespaceSlug,
    string RepositorySlug,
    string Description,
    bool IsPrivate,
    bool IsStarred,
    int StarCount,
    string Reason,
    DateTime UpdatedAtUtc);

/// <summary>当前用户可见的团队摘要。</summary>
public sealed record WorkspaceTeam(long Id, string Slug, string DisplayName, string Description, bool IsAdministrator);

/// <summary>稳定分页结果。</summary>
public sealed record WorkspacePage<T>(int Page, int PageSize, int TotalCount, IReadOnlyList<T> Items);

/// <summary>Dashboard 各模块独立降级的投影。</summary>
public sealed record WorkspaceModule<T>(T Value, bool IsAvailable = true, string? Error = null);

/// <summary>私人工作台完整投影。</summary>
public sealed record WorkspaceDashboard(
    WorkspaceModule<IReadOnlyList<WorkspaceTodo>> Todos,
    WorkspaceModule<IReadOnlyList<WorkspaceActivity>> Feed,
    WorkspaceModule<IReadOnlyList<WorkspaceRepository>> AttentionRepositories,
    WorkspaceModule<IReadOnlyList<WorkspaceNotification>> Notifications,
    WorkspaceModule<IReadOnlyList<WorkspaceTeam>> Teams,
    WorkspaceModule<IReadOnlyList<WorkspaceRepository>> Recommendations,
    int UnreadNotificationCount);

/// <summary>统一通知筛选。</summary>
public sealed record WorkspaceNotificationQuery(
    bool UnreadOnly = false,
    WorkspaceNotificationReason? Reason = null,
    WorkspaceResourceType? ResourceType = null,
    bool TeamOnly = false,
    int Page = 1,
    int PageSize = 25);

/// <summary>公开个人页允许的 tab。</summary>
public enum PublicProfileTab
{
    Repositories,
    Stars,
    Packages,
    Teams
}

/// <summary>公开个人页白名单投影。</summary>
public sealed record PublicProfile(
    string UserName,
    string DisplayName,
    string Description,
    PublicProfileTab Tab,
    WorkspacePage<WorkspaceRepository> Repositories,
    IReadOnlyList<WorkspaceTeam> Teams,
    int PackageCount);

/// <summary>公开发现页筛选。</summary>
public sealed record ExploreQuery(string? Search = null, string? License = null, int Page = 1, int PageSize = 25);

/// <summary>公开推荐页面。</summary>
public sealed record ExplorePage(
    WorkspacePage<WorkspaceRepository> Repositories,
    string AlgorithmVersion,
    DateTime? CalculatedAtUtc,
    bool IsFallback);
