namespace GitCandy.Git;

/// <summary>Git revision 解析结果。</summary>
public sealed record RepositoryRevision(
    string RequestedRevision,
    string CommitId,
    string DisplayName);

/// <summary>代码树条目类型。</summary>
public enum RepositoryTreeEntryKind
{
    Tree,
    Blob,
    Symlink,
    Submodule
}

/// <summary>代码树条目。</summary>
public sealed record RepositoryTreeEntry(
    string Name,
    string Path,
    RepositoryTreeEntryKind Kind,
    string ObjectId,
    long? Size);

/// <summary>代码树页面数据。</summary>
public sealed record RepositoryTreeResult(
    RepositoryRevision Revision,
    string Path,
    IReadOnlyList<RepositoryTreeEntry> Entries,
    IReadOnlyList<GitReferenceSnapshot> Branches,
    IReadOnlyList<GitReferenceSnapshot> Tags);

/// <summary>文本 blob 页面数据。</summary>
public sealed record RepositoryBlobResult(
    RepositoryRevision Revision,
    string Path,
    string Name,
    string ObjectId,
    long Size,
    bool IsBinary,
    bool IsTooLarge,
    bool HasUnknownEncoding,
    string? Text,
    string Language,
    int LineCount);

/// <summary>提交摘要。</summary>
public sealed record RepositoryCommitSummary(
    string Id,
    string Message,
    string MessageShort,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    IReadOnlyList<string> ParentIds);

/// <summary>分页提交历史。</summary>
public sealed record RepositoryCommitPage(
    RepositoryRevision Revision,
    int Page,
    int PageSize,
    bool HasNextPage,
    IReadOnlyList<RepositoryCommitSummary> Commits);

/// <summary>单文件 diff。</summary>
public sealed record RepositoryDiffFile(
    string Path,
    string? OldPath,
    string Status,
    bool IsBinary,
    int LinesAdded,
    int LinesDeleted,
    string? Patch);

/// <summary>提交及其 parent diff。</summary>
public sealed record RepositoryCommitResult(
    RepositoryCommitSummary Commit,
    IReadOnlyList<RepositoryDiffFile> Files,
    bool DiffTruncated);

/// <summary>Blame 连续行块。</summary>
public sealed record RepositoryBlameHunk(
    string CommitId,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt,
    int StartLine,
    IReadOnlyList<string> Lines);

/// <summary>文件 blame 数据。</summary>
public sealed record RepositoryBlameResult(
    RepositoryRevision Revision,
    string Path,
    IReadOnlyList<RepositoryBlameHunk> Hunks);

/// <summary>两个 revision 的 compare 数据。</summary>
public sealed record RepositoryCompareResult(
    RepositoryRevision Base,
    RepositoryRevision Head,
    int AheadBy,
    int BehindBy,
    IReadOnlyList<RepositoryCommitSummary> Commits,
    IReadOnlyList<RepositoryDiffFile> Files,
    bool DiffTruncated);

/// <summary>本地分支摘要。</summary>
public sealed record RepositoryBranchSummary(string Name, string CommitId, string Message, DateTimeOffset UpdatedAt, int AheadBy, int BehindBy, bool IsDefault);

/// <summary>Git tag 摘要。</summary>
public sealed record RepositoryTagSummary(string Name, string CommitId, bool IsAnnotated, string? TaggerName, DateTimeOffset? TaggedAt, string? Message);

/// <summary>不包含邮箱地址的 contributor 摘要。</summary>
public sealed record RepositoryContributorSummary(string Name, int CommitCount);

/// <summary>有界仓库统计结果。</summary>
public sealed record RepositoryStatisticsResult(RepositoryRevision Revision, int CommitCount, int ContributorCount, long FileCount, long SourceBytes, long RepositoryBytes, bool Truncated, IReadOnlyList<RepositoryContributorSummary> Contributors);
