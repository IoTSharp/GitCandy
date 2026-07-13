namespace GitCandy.Configuration;

/// <summary>Release 附件数量、大小和孤儿清理边界。</summary>
public sealed class ReleaseOptions
{
    public const string SectionName = "GitCandy:Releases";
    public long MaxAssetBytes { get; set; } = 100L * 1024 * 1024;
    public long MaxTotalAssetBytes { get; set; } = 1024L * 1024 * 1024;
    public int MaxAssetsPerRelease { get; set; } = 20;
    public TimeSpan OrphanRetention { get; set; } = TimeSpan.FromHours(24);
}
