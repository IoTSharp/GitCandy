using GitCandy.Application;

namespace GitCandy.Data.Domain;

/// <summary>改名、alias 延长和到期释放的审计事件。</summary>
public sealed class GitCandyRenameEvent
{
    public long Id { get; set; }

    public NameEventType EventType { get; set; }

    public NameSubjectType SubjectType { get; set; }

    public long SubjectId { get; set; }

    public string? ActorUserId { get; set; }

    public string OldSlug { get; set; } = string.Empty;

    public string NewSlug { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public string? Reason { get; set; }

    public bool IsOverride { get; set; }
}

/// <summary>名称生命周期审计事件类型。</summary>
public enum NameEventType
{
    Renamed = 1,
    AliasExtended = 2,
    AliasReleased = 3
}
