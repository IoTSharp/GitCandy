namespace GitCandy.Issues;

/// <summary>Issue 当前状态。</summary>
public enum IssueState
{
    Open,
    Closed
}

/// <summary>Issue 关系类型。</summary>
public enum IssueRelationType
{
    Related,
    Duplicate,
    Blocks
}

/// <summary>Issue timeline 事件类型。</summary>
public enum IssueEventType
{
    Created,
    Commented,
    Edited,
    Closed,
    Reopened,
    Assigned,
    Unassigned,
    Labeled,
    Unlabeled,
    Milestoned,
    Demilestoned,
    Subscribed,
    Unsubscribed,
    Locked,
    Unlocked,
    Hidden,
    Related,
    AutoClosed
}

/// <summary>Issue 通知类型。</summary>
public enum IssueNotificationType
{
    Mention,
    Assignment,
    Reply,
    Status,
    Relation
}

/// <summary>Issue 分页筛选条件。</summary>
public sealed record IssueQuery(
    IssueState State = IssueState.Open,
    string? Author = null,
    string? Assignee = null,
    string? Label = null,
    long? MilestoneId = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>创建 Issue 的输入。</summary>
public sealed record CreateIssueCommand(
    string Title,
    string Body,
    string AuthorUserId,
    string? AssigneeUserId = null,
    long? MilestoneId = null,
    IReadOnlyCollection<long>? LabelIds = null);

/// <summary>编辑 Issue 内容的输入。</summary>
public sealed record EditIssueCommand(string Title, string Body, long Version);

/// <summary>仓库 Issue 摘要。</summary>
public sealed record IssueSummary(
    long Id,
    long Number,
    string Title,
    IssueState State,
    string Author,
    string? Assignee,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int CommentCount,
    IReadOnlyList<IssueLabelSummary> Labels,
    string? Milestone);

/// <summary>Issue 分页结果。</summary>
public sealed record IssuePage(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<IssueSummary> Items);

/// <summary>Issue label 摘要。</summary>
public sealed record IssueLabelSummary(long Id, string Name, string Color, string Description, bool IsArchived);

/// <summary>Issue milestone 摘要。</summary>
public sealed record IssueMilestoneSummary(
    long Id,
    string Title,
    string Description,
    DateTime? DueAtUtc,
    bool IsClosed,
    bool IsArchived,
    int OpenIssues,
    int ClosedIssues);

/// <summary>Issue timeline 条目。</summary>
public sealed record IssueTimelineItem(
    long Id,
    long? CommentId,
    IssueEventType Type,
    string? Actor,
    string? BodyHtml,
    string? BodyMarkdown,
    string? Detail,
    DateTime CreatedAtUtc,
    DateTime? EditedAtUtc,
    bool IsHidden,
    bool CanEdit);

/// <summary>Issue 详情。</summary>
public sealed record IssueDetails(
    long Id,
    long RepositoryId,
    long Number,
    string Title,
    string BodyMarkdown,
    string BodyHtml,
    IssueState State,
    string AuthorUserId,
    string Author,
    string? AssigneeUserId,
    string? Assignee,
    long? MilestoneId,
    string? Milestone,
    bool IsLocked,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ClosedAtUtc,
    long Version,
    bool IsSubscribed,
    IReadOnlyList<IssueLabelSummary> Labels,
    IReadOnlyList<IssueTimelineItem> Timeline);

/// <summary>仓库 Issue metadata。</summary>
public sealed record IssueRepositoryMetadata(
    IReadOnlyList<IssueLabelSummary> Labels,
    IReadOnlyList<IssueMilestoneSummary> Milestones,
    IReadOnlyList<IssueAssigneeSummary> Assignees);

/// <summary>可指派用户。</summary>
public sealed record IssueAssigneeSummary(string UserId, string UserName);

/// <summary>站内 Issue 通知。</summary>
public sealed record IssueNotificationSummary(
    long Id,
    IssueNotificationType Type,
    string NamespaceSlug,
    string RepositorySlug,
    long IssueNumber,
    string IssueTitle,
    string? Actor,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc);

/// <summary>Issue template 内容。</summary>
public sealed record IssueTemplate(string Name, string Title, string Body);

/// <summary>Issue 修改结果。</summary>
public enum IssueMutationResult
{
    Succeeded,
    NotFound,
    Forbidden,
    Conflict,
    Invalid,
    Locked,
    RateLimited
}

/// <summary>Issue 输入不满足领域约束。</summary>
public sealed class IssueValidationException(string message) : Exception(message);

/// <summary>Issue 操作超过独立讨论速率限制。</summary>
public sealed class IssueRateLimitException(string message) : Exception(message);
