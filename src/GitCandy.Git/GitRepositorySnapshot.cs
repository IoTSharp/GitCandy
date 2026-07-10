namespace GitCandy.Git;

/// <summary>
/// 非协议热路径使用的轻量 Git 仓库快照。
/// </summary>
/// <param name="IsBare">是否为 bare 仓库。</param>
/// <param name="HeadCanonicalName">HEAD 指向的规范引用名。</param>
/// <param name="HeadCommitId">HEAD commit 的 SHA；空仓库时为空。</param>
/// <param name="LatestCommit">最近提交摘要；空仓库时为空。</param>
/// <param name="Branches">本地分支快照。</param>
/// <param name="Tags">标签快照。</param>
public sealed record GitRepositorySnapshot(
    bool IsBare,
    string HeadCanonicalName,
    string? HeadCommitId,
    GitCommitSnapshot? LatestCommit,
    IReadOnlyList<GitReferenceSnapshot> Branches,
    IReadOnlyList<GitReferenceSnapshot> Tags);

/// <summary>
/// Git 引用及其目标对象摘要。
/// </summary>
/// <param name="CanonicalName">规范引用名。</param>
/// <param name="TargetId">目标 Git object id；未解析时为空。</param>
public sealed record GitReferenceSnapshot(string CanonicalName, string? TargetId);

/// <summary>
/// Git commit 摘要。
/// </summary>
/// <param name="Id">commit SHA。</param>
/// <param name="MessageShort">短提交消息。</param>
/// <param name="AuthorName">作者名称。</param>
/// <param name="AuthorEmail">作者邮箱。</param>
/// <param name="AuthoredAt">作者时间。</param>
public sealed record GitCommitSnapshot(
    string Id,
    string MessageShort,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthoredAt);
