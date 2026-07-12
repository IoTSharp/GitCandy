namespace GitCandy.Configuration;

/// <summary>
/// GitCandy Identity 密码策略和外部登录配置。
/// </summary>
public sealed class GitCandyIdentityOptions
{
    public const string SectionName = "GitCandy:Identity";

    public GitCandyPasswordOptions Password { get; set; } = new();

    public GitCandyOpenIdConnectOptions OpenIdConnect { get; set; } = new();

    public GitCandyAccountRecoveryOptions AccountRecovery { get; set; } = new();
}

/// <summary>账号恢复 token、请求限流和 SMTP 投递配置。</summary>
public sealed class GitCandyAccountRecoveryOptions
{
    public TimeSpan TokenLifespan { get; set; } = TimeSpan.FromHours(1);

    public int MaxRequestsPerWindow { get; set; } = 5;

    public TimeSpan RequestWindow { get; set; } = TimeSpan.FromMinutes(15);

    public GitCandySmtpOptions Smtp { get; set; } = new();
}

/// <summary>账号邮件的 SMTP 投递配置。</summary>
public sealed class GitCandySmtpOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;
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
