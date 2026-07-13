namespace GitCandy.Teams;

/// <summary>
/// 团队治理角色。数值顺序只用于应用内角色比较，数据库持久化使用名称。
/// </summary>
public enum TeamRole
{
    Member = 0,
    DeputyLeader = 1,
    Leader = 2,
    TeamOwner = 3
}

/// <summary>
/// 由团队治理角色直接授予的团队级权限。
/// </summary>
public enum TeamPermission
{
    ViewEnterpriseConnections,
    ManageEnterpriseConnections,
    RenameTeam,
    TransferTeam,
    DeleteTeam,
    ManageTeamOwners,
    ManageLeaders,
    ManageDeputyLeaders,
    ManageMembers,
    CreateTeamRepository,
    ManageTeamPolicies
}

/// <summary>
/// 四级团队角色的固定权限矩阵。
/// </summary>
public static class TeamRolePermissions
{
    /// <summary>
    /// 判断角色是否具有指定团队级权限。
    /// 仓库成员管理仍由仓库权限决定，不在此矩阵中隐式授予。
    /// </summary>
    public static bool Allows(TeamRole role, TeamPermission permission) => permission switch
    {
        TeamPermission.ViewEnterpriseConnections => role is TeamRole.TeamOwner or TeamRole.Leader,
        TeamPermission.ManageEnterpriseConnections => role is TeamRole.TeamOwner,
        TeamPermission.RenameTeam => role is TeamRole.TeamOwner,
        TeamPermission.TransferTeam => role is TeamRole.TeamOwner,
        TeamPermission.DeleteTeam => role is TeamRole.TeamOwner,
        TeamPermission.ManageTeamOwners => role is TeamRole.TeamOwner,
        TeamPermission.ManageLeaders => role is TeamRole.TeamOwner,
        TeamPermission.ManageDeputyLeaders => role is TeamRole.TeamOwner or TeamRole.Leader,
        TeamPermission.ManageMembers => role is TeamRole.TeamOwner or TeamRole.Leader or TeamRole.DeputyLeader,
        TeamPermission.CreateTeamRepository => role is TeamRole.TeamOwner or TeamRole.Leader,
        TeamPermission.ManageTeamPolicies => role is TeamRole.TeamOwner or TeamRole.Leader,
        _ => false
    };

    /// <summary>
    /// 判断操作者能否管理目标角色。最后一位 TeamOwner 保护由持久化服务额外执行。
    /// </summary>
    public static bool CanManage(TeamRole actorRole, TeamRole targetRole) => targetRole switch
    {
        TeamRole.TeamOwner => Allows(actorRole, TeamPermission.ManageTeamOwners),
        TeamRole.Leader => Allows(actorRole, TeamPermission.ManageLeaders),
        TeamRole.DeputyLeader => Allows(actorRole, TeamPermission.ManageDeputyLeaders),
        TeamRole.Member => Allows(actorRole, TeamPermission.ManageMembers),
        _ => false
    };
}
