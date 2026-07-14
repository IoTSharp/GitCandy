namespace GitCandy.Application;

using GitCandy.Teams;

/// <summary>
/// 团队 CRUD 和成员管理的应用服务入口。
/// </summary>
public interface ITeamService
{
    /// <summary>查询团队列表。</summary>
    Task<IReadOnlyList<TeamSummary>> GetTeamsAsync(string? query, CancellationToken cancellationToken = default);

    /// <summary>读取团队详情。</summary>
    Task<TeamDetails?> GetTeamAsync(
        string teamName,
        string? viewerUserId,
        bool viewerIsAdministrator,
        CancellationToken cancellationToken = default);

    /// <summary>创建团队，并将创建者设为 TeamOwner。</summary>
    Task<bool> CreateTeamAsync(
        string name,
        string displayName,
        string description,
        string creatorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>更新团队资料。</summary>
    Task<bool> UpdateTeamAsync(
        string name,
        string displayName,
        string description,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default);

    /// <summary>删除团队及其级联关系。</summary>
    Task<bool> DeleteTeamAsync(
        string name,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default);

    /// <summary>添加、移除或调整团队成员。</summary>
    Task<bool> SetMemberAsync(
        string teamName,
        string userName,
        TeamMemberAction action,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default);

    /// <summary>在单个事务中批量调整团队成员。</summary>
    Task<TeamMemberChangeResult> ApplyMemberChangesAsync(
        string teamName,
        IReadOnlyList<TeamMemberChange> changes,
        string actorUserId,
        bool actorIsSystemAdministrator,
        CancellationToken cancellationToken = default);

    /// <summary>读取团队治理审计记录。</summary>
    Task<IReadOnlyList<TeamAuditEventSummary>> GetAuditEventsAsync(
        string teamName,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

/// <summary>团队成员变更动作。</summary>
public enum TeamMemberAction
{
    Add,
    Remove,
    MakeTeamOwner,
    MakeLeader,
    MakeDeputyLeader,
    MakeMember
}

/// <summary>单个成员治理变更。</summary>
public sealed record TeamMemberChange(string UserName, TeamMemberAction Action);

/// <summary>批量成员治理结果。</summary>
public sealed record TeamMemberChangeResult(bool Succeeded, string? Error = null)
{
    public static TeamMemberChangeResult Success { get; } = new(true);
}

/// <summary>团队列表摘要。</summary>
public sealed record TeamSummary(string Name, string DisplayName, string Description, int MemberCount);

/// <summary>团队成员摘要。</summary>
public sealed record TeamMemberSummary(string UserName, string DisplayName, TeamRole Role)
{
    public bool IsAdministrator => Role == TeamRole.TeamOwner;
}

/// <summary>不包含敏感数据的团队治理审计摘要。</summary>
public sealed record TeamAuditEventSummary(
    string Actor,
    string Action,
    string Outcome,
    string Subject,
    string Detail,
    DateTimeOffset OccurredAt);

/// <summary>团队详情。</summary>
public sealed record TeamDetails(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<TeamMemberSummary> Members,
    IReadOnlyList<string> Repositories);
