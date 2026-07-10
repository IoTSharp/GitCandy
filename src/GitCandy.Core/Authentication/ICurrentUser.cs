using System.Security.Claims;

namespace GitCandy.Authentication;

/// <summary>
/// 当前 ASP.NET Core 请求用户的只读访问入口。
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// 当前请求的声明主体。
    /// </summary>
    ClaimsPrincipal Principal { get; }

    /// <summary>
    /// 当前用户是否已经通过认证。
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// 当前 Identity 用户主键；匿名请求返回 <see langword="null" />。
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// 当前 Identity 用户名；匿名请求返回 <see langword="null" />。
    /// </summary>
    string? UserName { get; }

    /// <summary>
    /// 当前用户是否具有系统管理员角色。
    /// </summary>
    bool IsAdministrator { get; }

    /// <summary>
    /// 当前请求中止令牌；没有活动请求时为 <see cref="CancellationToken.None" />。
    /// </summary>
    CancellationToken RequestAborted { get; }
}
