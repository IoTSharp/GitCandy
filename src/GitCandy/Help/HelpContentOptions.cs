namespace GitCandy.Help;

/// <summary>
/// 描述构建阶段生成的帮助站点内容目录。
/// </summary>
public sealed class HelpContentOptions
{
    /// <summary>
    /// 获取或设置测试 host 的帮助站点目录。生产 host 固定读取应用输出中的 <c>wwwroot/help</c>。
    /// </summary>
    public string? ContentPath { get; set; }
}
