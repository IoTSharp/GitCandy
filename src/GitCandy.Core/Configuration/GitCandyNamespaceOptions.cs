namespace GitCandy.Configuration;

/// <summary>
/// 稳定命名空间、历史地址和改名限频配置。
/// </summary>
public sealed class GitCandyNamespaceOptions
{
    /// <summary>标准配置节名称。</summary>
    public const string SectionName = "GitCandy:Namespaces";

    /// <summary>历史名称默认保留天数。</summary>
    public int AliasRetentionDays { get; set; } = 365;

    /// <summary>滚动窗口内允许成功修改 namespace slug 的次数。</summary>
    public int RenameLimit { get; set; } = 3;

    /// <summary>改名限频滚动窗口天数。</summary>
    public int RenameWindowDays { get; set; } = 7;
}
