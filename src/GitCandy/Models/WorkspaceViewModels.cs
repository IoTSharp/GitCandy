using GitCandy.Workspace;

namespace GitCandy.Models;

public sealed record WorkspaceIndexViewModel(string UserName, WorkspaceDashboard Dashboard);
public sealed record TodoIndexViewModel(WorkspacePage<WorkspaceTodo> Page);
public sealed record NotificationIndexViewModel(WorkspacePage<WorkspaceNotification> Page, WorkspaceNotificationQuery Query);
public sealed record WorkspaceRepositoriesViewModel(string Title, string Description, IReadOnlyList<WorkspaceRepository> Repositories);
public sealed record WorkspaceTeamsViewModel(IReadOnlyList<WorkspaceTeam> Teams);
public sealed record PublicProfileViewModel(PublicProfile Profile);
public sealed record ExploreViewModel(ExplorePage Page, ExploreQuery Query);
