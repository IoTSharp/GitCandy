namespace GitCandy.Data.Domain;

public sealed class GitCandyRelease
{
    public long Id { get; set; }
    public long RepositoryId { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string NormalizedTagName { get; set; } = string.Empty;
    public string TagCommitSha { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BodyMarkdown { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public bool IsDraft { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public GitCandyRepository? Repository { get; set; }
    public ICollection<GitCandyReleaseAsset> Assets { get; } = [];
}

public sealed class GitCandyReleaseAsset
{
    public string Id { get; set; } = string.Empty;
    public long ReleaseId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public long DownloadCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public GitCandyRelease? Release { get; set; }
}
