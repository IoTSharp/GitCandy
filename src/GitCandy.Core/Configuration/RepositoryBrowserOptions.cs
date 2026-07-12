namespace GitCandy.Configuration;

/// <summary>
/// 仓库代码浏览、diff、归档和 LFS 的资源边界。
/// </summary>
public sealed class RepositoryBrowserOptions
{
    /// <summary>标准配置节名称。</summary>
    public const string SectionName = "GitCandy:RepositoryBrowser";

    /// <summary>ASP.NET Core request timeout policy 名称。</summary>
    public const string RequestTimeoutPolicyName = "GitCandy.RepositoryBrowser";

    /// <summary>允许在页面中显示的最大 blob 字节数。</summary>
    public long MaxDisplayedBlobBytes { get; set; } = 1024 * 1024;

    /// <summary>单次 diff 允许返回的最大文本字符数。</summary>
    public int MaxDiffCharacters { get; set; } = 2 * 1024 * 1024;

    /// <summary>单次 diff 允许包含的最大文件数。</summary>
    public int MaxDiffFiles { get; set; } = 500;

    /// <summary>单个归档允许写入的最大未压缩字节数。</summary>
    public long MaxArchiveBytes { get; set; } = 1024L * 1024 * 1024;

    /// <summary>单个归档允许包含的最大条目数。</summary>
    public int MaxArchiveEntries { get; set; } = 100_000;

    /// <summary>仓库读取操作的最长执行时间。</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Contributors 统计最多遍历的提交数。</summary>
    public int MaxStatisticsCommits { get; set; } = 10000;

    /// <summary>Contributors 页面最多显示的作者数。</summary>
    public int MaxContributors { get; set; } = 100;
}

/// <summary>
/// Git LFS v2 对象存储配置。
/// </summary>
public sealed class GitLfsOptions
{
    /// <summary>标准配置节名称。</summary>
    public const string SectionName = "GitCandy:Lfs";

    /// <summary>是否启用 Git LFS HTTP API。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>单个 LFS 对象允许的最大字节数。</summary>
    public long MaxObjectBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>单仓库允许保存的 LFS 对象总字节数；0 表示不单独限制。</summary>
    public long RepositoryQuotaBytes { get; set; }

    /// <summary>流复制缓冲区大小。</summary>
    public int StreamBufferSize { get; set; } = 128 * 1024;

    /// <summary>上传和下载的最长执行时间。</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(30);
}
