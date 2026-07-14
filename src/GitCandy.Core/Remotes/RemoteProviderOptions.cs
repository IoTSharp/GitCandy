namespace GitCandy.Remotes;

/// <summary>远程 Git provider 与出站请求的宿主配置。</summary>
public sealed class RemoteProviderOptions
{
    public const string SectionName = "GitCandy:Remotes";

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(20);

    public RemoteProviderEndpointOptions GitHub { get; set; } = new()
    {
        ServerUrl = "https://github.com",
        ApiBaseUrl = "https://api.github.com"
    };

    public RemoteProviderEndpointOptions GitLab { get; set; } = new()
    {
        ServerUrl = "https://gitlab.com",
        ApiBaseUrl = "https://gitlab.com/api/v4"
    };

    public RemoteProviderEndpointOptions Gitee { get; set; } = new()
    {
        ServerUrl = "https://gitee.com",
        ApiBaseUrl = "https://gitee.com/api/v5"
    };

    public RemoteProviderEndpointOptions Get(RemoteProviderKind kind) => kind switch
    {
        RemoteProviderKind.GitHub => GitHub,
        RemoteProviderKind.GitLab => GitLab,
        RemoteProviderKind.Gitee => Gitee,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported remote provider.")
    };
}

/// <summary>由管理员固定的 provider Web/API 端点，普通用户不能覆盖。</summary>
public sealed class RemoteProviderEndpointOptions
{
    public bool Enabled { get; set; } = true;

    public string ServerUrl { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = string.Empty;
}
