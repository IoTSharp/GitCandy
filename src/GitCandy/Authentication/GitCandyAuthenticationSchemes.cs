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

    /// <summary>HTTP API 使用的 Bearer PAT 认证方案。</summary>
    public const string PersonalAccessToken = "GitCandy.PersonalAccessToken";

    /// <summary>
    /// 可选的通用 OpenID Connect Web 登录方案。
    /// </summary>
    public const string OpenIdConnect = "GitCandy.OpenIdConnect";

    /// <summary>SCIM 2.0 provisioning endpoint 的独立 Bearer 方案。</summary>
    public const string ScimBearer = "GitCandy.ScimBearer";
}
