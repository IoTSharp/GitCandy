namespace GitCandy.Configuration;

/// <summary>
/// Git Smart HTTP 请求限制和流式传输配置。
/// </summary>
public sealed class GitSmartHttpOptions
{
    /// <summary>
    /// 标准配置节名称。
    /// </summary>
    public const string SectionName = "GitCandy:GitHttp";

    /// <summary>
    /// 单个 Git Smart HTTP 请求允许的最大请求体字节数。
    /// </summary>
    public long MaxRequestBodySize { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// 单个 Git helper 操作的最长执行时间。
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 请求、响应与 Git helper 管道之间的流式复制缓冲区大小。
    /// </summary>
    public int StreamBufferSize { get; set; } = 81920;

    /// <summary>
    /// 单个 GitCandy 进程允许同时运行的 Git transport helper 数量。
    /// </summary>
    public int MaxConcurrentOperations { get; set; } = 16;
}
