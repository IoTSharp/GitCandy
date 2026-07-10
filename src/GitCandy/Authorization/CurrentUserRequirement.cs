using Microsoft.AspNetCore.Authorization;

namespace GitCandy.Authorization;

/// <summary>
/// 当前用户自身或系统管理员授权要求。
/// </summary>
public sealed class CurrentUserRequirement : IAuthorizationRequirement;
