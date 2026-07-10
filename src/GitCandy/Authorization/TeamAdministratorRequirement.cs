using Microsoft.AspNetCore.Authorization;

namespace GitCandy.Authorization;

/// <summary>
/// 团队管理员或系统管理员授权要求。
/// </summary>
public sealed class TeamAdministratorRequirement : IAuthorizationRequirement;
