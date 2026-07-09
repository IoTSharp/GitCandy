using Microsoft.AspNetCore.Identity;

namespace GitCandy.Data.Identity;

/// <summary>
/// GitCandy 新系统的 Identity 用户。
/// </summary>
public sealed class GitCandyUser : IdentityUser
{
    /// <summary>
    /// 用户在页面中显示的名称。
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 用户个人说明。
    /// </summary>
    public string? Description { get; set; }
}
