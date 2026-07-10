namespace GitCandy.Configuration;

/// <summary>
/// 外部 OpenSSH forced-command 可选适配配置。
/// </summary>
public sealed class GitCandyOpenSshOptions
{
    /// <summary>
    /// 标准配置节名称。
    /// </summary>
    public const string SectionName = "GitCandy:OpenSsh";

    /// <summary>
    /// 是否允许 OpenSSH 调用 key 查询和 forced-command 入口。默认关闭。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// OpenSSH host 可执行的 GitCandy self-contained 程序绝对路径。
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;
}
