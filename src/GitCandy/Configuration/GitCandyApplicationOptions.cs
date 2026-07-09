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
    /// 旧 Web.config 日志路径配置键，仅作为迁移期别名读取。
    /// </summary>
    public const string LegacyLogPathFormatKey = "LogPathFormat";

    /// <summary>
    /// 旧 Web.config 用户配置 XML 路径键，仅作为迁移期别名读取。
    /// </summary>
    public const string LegacyUserConfigurationKey = "UserConfiguration";

    /// <summary>
    /// 日志文件路径格式，参数 0 为日期字符串。
    /// </summary>
    public string LogPathFormat { get; set; } = "App_Data/{0}.log";

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
    /// 仓库存储路径。路径解析和边界检查由后续路径配置切片完成。
    /// </summary>
    public string RepositoryPath { get; set; } = "App_Data/Repos";

    /// <summary>
    /// GitCandy 缓存路径。路径解析和边界检查由后续路径配置切片完成。
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

    /// <summary>
    /// 内置 SSH 服务监听端口。
    /// </summary>
    public int SshPort { get; set; } = 22;

    /// <summary>
    /// 是否启用内置 SSH 服务。
    /// </summary>
    public bool EnableSsh { get; set; } = true;
}
