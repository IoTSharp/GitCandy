namespace GitCandy.Git;

/// <summary>
/// 解析 Git transport 和运维检查共同使用的 Git 可执行文件。
/// </summary>
public interface IGitExecutableResolver
{
    /// <summary>
    /// 返回配置的 Git 可执行文件路径或 PATH 中的命令名。
    /// </summary>
    /// <returns>Git 可执行文件路径或命令名。</returns>
    string Resolve();
}
