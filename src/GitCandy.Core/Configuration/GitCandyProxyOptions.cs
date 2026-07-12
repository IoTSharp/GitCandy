namespace GitCandy.Configuration;

/// <summary>反向代理转发头的显式信任边界。</summary>
public sealed class GitCandyProxyOptions
{
    public const string SectionName = "GitCandy:Proxy";

    public bool Enabled { get; set; }

    public int ForwardLimit { get; set; } = 1;

    public string[] KnownProxies { get; set; } = [];
}
