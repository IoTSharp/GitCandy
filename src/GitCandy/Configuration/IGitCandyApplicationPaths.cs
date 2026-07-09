namespace GitCandy.Configuration;

/// <summary>
/// GitCandy 应用路径配置的解析结果。
/// </summary>
public interface IGitCandyApplicationPaths
{
    /// <summary>
    /// 应用内容根目录。
    /// </summary>
    string ContentRootPath { get; }

    /// <summary>
    /// 应用 Web 根目录。
    /// </summary>
    string? WebRootPath { get; }

    /// <summary>
    /// 日志文件路径格式，参数 0 为日期字符串。
    /// </summary>
    string LogPathFormat { get; }

    /// <summary>
    /// 旧用户配置 XML 的绝对路径。
    /// </summary>
    string UserConfigurationPath { get; }

    /// <summary>
    /// 仓库存储根目录的绝对路径。
    /// </summary>
    string RepositoryPath { get; }

    /// <summary>
    /// GitCandy 缓存根目录的绝对路径。
    /// </summary>
    string CachePath { get; }

    /// <summary>
    /// Git 官方 helper 所在目录的绝对路径。未配置时为空字符串。
    /// </summary>
    string GitCorePath { get; }

    /// <summary>
    /// 将配置路径解析为基于内容根目录的绝对路径。
    /// </summary>
    /// <param name="configuredPath">配置中的路径值。</param>
    /// <returns>绝对路径。</returns>
    string ResolveContentPath(string configuredPath);

    /// <summary>
    /// 将配置路径解析为基于 Web 根目录的绝对路径。
    /// </summary>
    /// <param name="configuredPath">配置中的路径值。</param>
    /// <returns>绝对路径。</returns>
    string ResolveWebRootPath(string configuredPath);
}
