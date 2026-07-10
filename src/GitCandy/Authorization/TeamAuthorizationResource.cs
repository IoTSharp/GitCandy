namespace GitCandy.Authorization;

/// <summary>
/// 团队授权资源。
/// </summary>
/// <param name="TeamName">团队名称。</param>
public sealed record TeamAuthorizationResource(string TeamName);
