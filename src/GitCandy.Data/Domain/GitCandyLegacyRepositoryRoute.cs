namespace GitCandy.Data.Domain;

/// <summary>旧 <c>/git/{project}</c> 地址到稳定仓库 ID 的显式映射。</summary>
public sealed class GitCandyLegacyRepositoryRoute
{
    public long Id { get; set; }

    public string Project { get; set; } = string.Empty;

    public string NormalizedProject { get; set; } = string.Empty;

    public long RepositoryId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public GitCandyRepository? Repository { get; set; }
}
