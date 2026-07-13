using GitCandy.Workspace;
using GitCandy.Notifications;
using System.ComponentModel.DataAnnotations;

namespace GitCandy.Models;

public sealed record WorkspaceIndexViewModel(string UserName, WorkspaceDashboard Dashboard);
public sealed record TodoIndexViewModel(WorkspacePage<WorkspaceTodo> Page);
public sealed record NotificationIndexViewModel(WorkspacePage<WorkspaceNotification> Page, WorkspaceNotificationQuery Query);
public sealed record NotificationPreferencesViewModel(
    IReadOnlyList<NotificationPreference> Preferences,
    IReadOnlyList<NotificationDeliveryDiagnostic> Deliveries,
    NotificationPreferenceFormViewModel Edit);

public sealed class NotificationPreferenceFormViewModel
{
    public WorkspaceNotificationEventType EventType { get; set; }
    public bool EmailEnabled { get; set; }
    public bool WebhookEnabled { get; set; }
    public string? WebhookUrl { get; set; }
    [StringLength(512)]
    public string? WebhookSecret { get; set; }
}
public sealed record WorkspaceRepositoriesViewModel(string Title, string Description, IReadOnlyList<WorkspaceRepository> Repositories);
public sealed record WorkspaceTeamsViewModel(IReadOnlyList<WorkspaceTeam> Teams);
public sealed record PublicProfileViewModel(PublicProfile Profile);
public sealed record ExploreViewModel(ExplorePage Page, ExploreQuery Query);
