namespace GitCandy.Issues;

/// <summary>仓库 Issue、讨论、元数据、订阅和通知的统一应用服务。</summary>
public interface IIssueService
{
    /// <summary>按仓库、状态和 metadata 条件分页查询 Issue。</summary>
    Task<IssuePage> GetIssuesAsync(long repositoryId, IssueQuery query, CancellationToken cancellationToken = default);
    /// <summary>读取单个 Issue 及其 timeline。</summary>
    Task<IssueDetails?> GetIssueAsync(long repositoryId, long number, string? viewerUserId, CancellationToken cancellationToken = default);
    /// <summary>在仓库事务内分配编号并创建 Issue。</summary>
    Task<IssueDetails> CreateIssueAsync(long repositoryId, CreateIssueCommand command, CancellationToken cancellationToken = default);
    /// <summary>以乐观并发版本编辑 Issue 内容。</summary>
    Task<IssueMutationResult> EditIssueAsync(long repositoryId, long number, string actorUserId, bool isOwner, EditIssueCommand command, CancellationToken cancellationToken = default);
    /// <summary>关闭或重新打开 Issue。</summary>
    Task<IssueMutationResult> SetStateAsync(long repositoryId, long number, string actorUserId, bool isOwner, IssueState state, CancellationToken cancellationToken = default);
    /// <summary>向未锁定的 Issue 讨论添加评论。</summary>
    Task<IssueMutationResult> AddCommentAsync(long repositoryId, long number, string actorUserId, bool isOwner, string body, CancellationToken cancellationToken = default);
    /// <summary>编辑评论并保留编辑历史。</summary>
    Task<IssueMutationResult> EditCommentAsync(long repositoryId, long number, long commentId, string actorUserId, bool isOwner, string body, CancellationToken cancellationToken = default);
    /// <summary>隐藏评论并保留审计 timeline。</summary>
    Task<IssueMutationResult> HideCommentAsync(long repositoryId, long number, long commentId, string actorUserId, bool isOwner, CancellationToken cancellationToken = default);
    /// <summary>锁定或解锁 Issue 讨论。</summary>
    Task<IssueMutationResult> SetLockedAsync(long repositoryId, long number, string actorUserId, bool isOwner, bool locked, CancellationToken cancellationToken = default);
    /// <summary>订阅或退订 Issue 通知。</summary>
    Task<IssueMutationResult> SetSubscriptionAsync(long repositoryId, long number, string userId, bool subscribed, CancellationToken cancellationToken = default);
    /// <summary>设置仓库协作者负责人。</summary>
    Task<IssueMutationResult> SetAssigneeAsync(long repositoryId, long number, string actorUserId, bool isOwner, string? assigneeUserId, CancellationToken cancellationToken = default);
    /// <summary>设置或清除 Issue milestone。</summary>
    Task<IssueMutationResult> SetMilestoneAsync(long repositoryId, long number, string actorUserId, bool isOwner, long? milestoneId, CancellationToken cancellationToken = default);
    /// <summary>添加或移除 Issue label。</summary>
    Task<IssueMutationResult> SetLabelAsync(long repositoryId, long number, string actorUserId, bool isOwner, long labelId, bool selected, CancellationToken cancellationToken = default);
    /// <summary>建立仓库内 Issue 关系。</summary>
    Task<IssueMutationResult> AddRelationAsync(long repositoryId, long number, long targetNumber, string actorUserId, bool isOwner, IssueRelationType relationType, CancellationToken cancellationToken = default);
    /// <summary>读取仓库 Issue metadata 和可指派协作者。</summary>
    Task<IssueRepositoryMetadata> GetMetadataAsync(long repositoryId, CancellationToken cancellationToken = default);
    /// <summary>创建或更新仓库 label。</summary>
    Task<IssueLabelSummary?> SaveLabelAsync(long repositoryId, long? labelId, string name, string color, string description, bool isOwner, CancellationToken cancellationToken = default);
    /// <summary>创建或更新仓库 milestone。</summary>
    Task<IssueMilestoneSummary?> SaveMilestoneAsync(long repositoryId, long? milestoneId, string title, string description, DateTime? dueAtUtc, bool isOwner, CancellationToken cancellationToken = default);
    /// <summary>归档 label，同时保留历史关联。</summary>
    Task<IssueMutationResult> ArchiveLabelAsync(long repositoryId, long labelId, bool isOwner, CancellationToken cancellationToken = default);
    /// <summary>关闭或归档 milestone，同时保留历史关联。</summary>
    Task<IssueMutationResult> SetMilestoneStatusAsync(long repositoryId, long milestoneId, bool closed, bool archived, bool isOwner, CancellationToken cancellationToken = default);
    /// <summary>读取当前仍有权限访问的站内通知。</summary>
    Task<IReadOnlyList<IssueNotificationSummary>> GetNotificationsAsync(string userId, bool isAdministrator, CancellationToken cancellationToken = default);
    /// <summary>在复核权限后将通知标为已读。</summary>
    Task MarkNotificationReadAsync(long notificationId, string userId, CancellationToken cancellationToken = default);
    /// <summary>幂等应用默认分支提交或 PR merge 的自动关闭引用。</summary>
    Task<int> ApplyClosingReferencesAsync(long repositoryId, string actorUserId, string text, string source, CancellationToken cancellationToken = default);
}

/// <summary>安全 Markdown 渲染入口。</summary>
public interface IIssueMarkdownRenderer
{
    /// <summary>渲染并清洗仓库上下文内的 Markdown。</summary>
    string Render(string markdown, string namespaceSlug, string repositorySlug);
}

/// <summary>从受控仓库路径读取 Issue template。</summary>
public interface IIssueTemplateService
{
    /// <summary>从受控模板目录读取模板；缺失或无效时返回空。</summary>
    Task<IssueTemplate?> GetTemplateAsync(string repositoryStorageName, string? name, CancellationToken cancellationToken = default);
}
