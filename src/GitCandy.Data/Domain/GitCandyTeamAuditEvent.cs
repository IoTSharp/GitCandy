namespace GitCandy.Data.Domain;

/// <summary>不可变团队治理审计事件。</summary>
public sealed class GitCandyTeamAuditEvent
{
    public long Id { get; set; }
    public long? TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}
