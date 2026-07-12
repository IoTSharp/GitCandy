namespace GitCandy.Git;

/// <summary>
/// 仓库 tree、blob、历史、diff、blame、compare 和归档的统一只读入口。
/// </summary>
public interface IRepositoryBrowserService
{
    /// <summary>读取指定 revision 和路径的 tree。</summary>
    RepositoryTreeResult? ReadTree(
        GitRepositoryContext repository,
        string? revision,
        string? path,
        CancellationToken cancellationToken = default);

    /// <summary>读取指定 revision 和路径的 blob 展示数据。</summary>
    RepositoryBlobResult? ReadBlob(
        GitRepositoryContext repository,
        string? revision,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>将原始 blob 流式复制到输出。</summary>
    Task<RepositoryBlobResult?> CopyBlobAsync(
        GitRepositoryContext repository,
        string? revision,
        string path,
        Stream output,
        CancellationToken cancellationToken = default);

    /// <summary>分页读取提交历史。</summary>
    RepositoryCommitPage? ReadCommits(
        GitRepositoryContext repository,
        string? revision,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>读取提交详情和 parent diff。</summary>
    RepositoryCommitResult? ReadCommit(
        GitRepositoryContext repository,
        string commitId,
        CancellationToken cancellationToken = default);

    /// <summary>读取指定文本文件的 blame。</summary>
    RepositoryBlameResult? ReadBlame(
        GitRepositoryContext repository,
        string? revision,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>比较两个 branch、tag 或 commit。</summary>
    RepositoryCompareResult? Compare(
        GitRepositoryContext repository,
        string baseRevision,
        string headRevision,
        CancellationToken cancellationToken = default);

    /// <summary>将指定 revision 以 ZIP 格式流式写入输出。</summary>
    Task<RepositoryRevision?> WriteArchiveAsync(
        GitRepositoryContext repository,
        string? revision,
        Stream output,
        CancellationToken cancellationToken = default);

    /// <summary>读取本地分支及其相对默认分支的 ahead/behind。</summary>
    IReadOnlyList<RepositoryBranchSummary> ReadBranches(GitRepositoryContext repository, CancellationToken cancellationToken = default);

    /// <summary>读取 lightweight 与 annotated tags。</summary>
    IReadOnlyList<RepositoryTagSummary> ReadTags(GitRepositoryContext repository, CancellationToken cancellationToken = default);

    /// <summary>按 revision 计算有界且不公开邮箱的 contributor 与仓库统计。</summary>
    RepositoryStatisticsResult? ReadStatistics(GitRepositoryContext repository, string? revision, CancellationToken cancellationToken = default);
}
