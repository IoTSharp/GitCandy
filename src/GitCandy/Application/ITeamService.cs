namespace GitCandy.Application;

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

    /// <summary>创建团队，并将创建者设为团队管理员。</summary>
    Task<bool> CreateTeamAsync(
        string name,
        string description,
        string creatorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>更新团队资料。</summary>
    Task<bool> UpdateTeamAsync(string name, string description, CancellationToken cancellationToken = default);

    /// <summary>删除团队及其级联关系。</summary>
    Task<bool> DeleteTeamAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>添加、移除或调整团队成员。</summary>
    Task<bool> SetMemberAsync(
        string teamName,
        string userName,
        TeamMemberAction action,
        CancellationToken cancellationToken = default);
}

/// <summary>团队成员变更动作。</summary>
public enum TeamMemberAction
{
    Add,
    Remove,
    MakeAdministrator,
    MakeMember
}

/// <summary>团队列表摘要。</summary>
public sealed record TeamSummary(string Name, string Description, int MemberCount);

/// <summary>团队成员摘要。</summary>
public sealed record TeamMemberSummary(string UserName, string DisplayName, bool IsAdministrator);

/// <summary>团队详情。</summary>
public sealed record TeamDetails(
    string Name,
    string Description,
    IReadOnlyList<TeamMemberSummary> Members,
    IReadOnlyList<string> Repositories);
