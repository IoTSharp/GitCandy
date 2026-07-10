namespace GitCandy.Authentication;

/// <summary>
/// GitCandy 认证方案名称。
/// </summary>
public static class GitCandyAuthenticationSchemes
{
    /// <summary>
    /// Git Smart HTTP 使用的独立 HTTP Basic 认证方案。
    /// </summary>
    public const string GitBasic = "GitCandy.GitBasic";

    /// <summary>
    /// 可选的通用 OpenID Connect Web 登录方案。
    /// </summary>
    public const string OpenIdConnect = "GitCandy.OpenIdConnect";
}
