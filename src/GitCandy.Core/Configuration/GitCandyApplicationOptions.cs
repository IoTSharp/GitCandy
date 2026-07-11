namespace GitCandy.Configuration;

/// <summary>
/// GitCandy 应用级配置。
/// </summary>
public sealed class GitCandyApplicationOptions
{
    /// <summary>
    /// 标准配置节名称。
    /// </summary>
    public const string SectionName = "GitCandy:Application";

    /// <summary>
    /// 旧 Web.config 用户配置 XML 路径键，仅作为迁移期别名读取。
    /// </summary>
    public const string LegacyUserConfigurationKey = "UserConfiguration";

    /// <summary>
    /// 旧用户配置 XML 的保留路径，用于后续独立导入或兼容读取。
    /// </summary>
    public string UserConfigurationPath { get; set; } = "App_Data/config.xml";

    /// <summary>
    /// 是否允许匿名用户读取公开仓库。
    /// </summary>
    public bool IsPublicServer { get; set; } = true;

    /// <summary>
    /// 是否强制 Web UI 使用 HTTPS。
    /// </summary>
    public bool ForceSsl { get; set; }

    /// <summary>
    /// ForceSsl 启用时使用的 HTTPS 端口。
    /// </summary>
    public int SslPort { get; set; } = 443;

    /// <summary>
    /// 本地请求是否跳过自定义错误页。
    /// </summary>
    public bool LocalSkipCustomError { get; set; } = true;

    /// <summary>
    /// 是否允许用户自助注册。
    /// </summary>
    public bool AllowRegisterUser { get; set; } = true;

    /// <summary>
    /// 是否允许普通用户创建仓库。
    /// </summary>
    public bool AllowRepositoryCreation { get; set; } = true;

    /// <summary>
    /// 仓库存储路径。运行时通过 <see cref="IGitCandyApplicationPaths" /> 解析为绝对路径。
    /// </summary>
    public string RepositoryPath { get; set; } = "App_Data/Repos";

    /// <summary>
    /// GitCandy 缓存路径。运行时通过 <see cref="IGitCandyApplicationPaths" /> 解析为绝对路径。
    /// </summary>
    public string CachePath { get; set; } = "App_Data/Caches";

    /// <summary>
    /// Git 官方 helper 所在目录。为空时由后续 Git backend 配置切片处理发现逻辑。
    /// </summary>
    public string GitCorePath { get; set; } = string.Empty;

    /// <summary>
    /// 提交列表每页显示数量。
    /// </summary>
    public int NumberOfCommitsPerPage { get; set; } = 30;

    /// <summary>
    /// 普通列表每页显示数量。
    /// </summary>
    public int NumberOfItemsPerList { get; set; } = 30;

    /// <summary>
    /// 仓库贡献者统计显示数量。
    /// </summary>
    public int NumberOfRepositoryContributors { get; set; } = 50;

    /// <summary>是否允许 Pull Request 作者批准本人提交的变更。</summary>
    public bool AllowAuthorApproval { get; set; }

    /// <summary>source branch 更新后是否令旧 head 上的批准失效。</summary>
    public bool DismissStalePullRequestApprovals { get; set; } = true;

    /// <summary>允许合并 Pull Request 所需的有效批准数；M13 branch policy 可在此基线上扩展。</summary>
    public int RequiredPullRequestApprovals { get; set; } = 1;

    /// <summary>
    /// 内置 SSH 服务监听端口。
    /// </summary>
    public int SshPort { get; set; } = 22;

    /// <summary>
    /// 是否启用内置 SSH 服务。
    /// </summary>
    public bool EnableSsh { get; set; } = true;

    /// <summary>
    /// 内置 SSH server host key 文件路径。相对路径基于应用 content root。
    /// </summary>
    public string SshHostKeyPath { get; set; } = "App_Data/ssh-host-key.xml";

    /// <summary>
    /// ASP.NET Core Data Protection key ring 路径。必须持久化以保证重启后 Identity cookie 可继续使用。
    /// </summary>
    public string DataProtectionKeysPath { get; set; } = "App_Data/DataProtectionKeys";
}
