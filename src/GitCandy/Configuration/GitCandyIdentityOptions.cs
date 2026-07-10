namespace GitCandy.Configuration;

/// <summary>
/// GitCandy Identity 密码策略和外部登录配置。
/// </summary>
public sealed class GitCandyIdentityOptions
{
    public const string SectionName = "GitCandy:Identity";

    public GitCandyPasswordOptions Password { get; set; } = new();

    public GitCandyOpenIdConnectOptions OpenIdConnect { get; set; } = new();
}

/// <summary>
/// Identity 密码复杂度策略。
/// </summary>
public sealed class GitCandyPasswordOptions
{
    public int RequiredLength { get; set; } = 12;

    public int RequiredUniqueChars { get; set; } = 4;

    public bool RequireDigit { get; set; } = true;

    public bool RequireLowercase { get; set; } = true;

    public bool RequireUppercase { get; set; } = true;

    public bool RequireNonAlphanumeric { get; set; } = true;
}

/// <summary>
/// 通用 OpenID Connect 外部登录配置。
/// </summary>
public sealed class GitCandyOpenIdConnectOptions
{
    public bool Enabled { get; set; }

    public string DisplayName { get; set; } = "OpenID Connect";

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string CallbackPath { get; set; } = "/signin-oidc";

    public bool RequireHttpsMetadata { get; set; } = true;
}
