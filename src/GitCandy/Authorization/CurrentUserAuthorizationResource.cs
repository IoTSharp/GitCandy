namespace GitCandy.Authorization;

/// <summary>
/// 当前用户授权资源。
/// </summary>
/// <param name="UserName">目标用户名；为空表示当前用户自身。</param>
public sealed record CurrentUserAuthorizationResource(string? UserName);
