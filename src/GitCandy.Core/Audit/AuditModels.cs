namespace GitCandy.Audit;

/// <summary>协作审计事件的操作者类型。</summary>
public enum AuditActorType
{
    User,
    DeployKey,
    Credential,
    System
}

/// <summary>不可变协作审计事件的只读投影。</summary>
public sealed record AuditEvent(
    long Id,
    string Source,
    AuditActorType ActorType,
    string Actor,
    string Action,
    string Outcome,
    string Reference,
    string Detail,
    DateTimeOffset OccurredAt);

/// <summary>仓库 owner 查询安全与协作审计证据的边界。</summary>
public interface IAuditLogService
{
    Task<IReadOnlyList<AuditEvent>> GetRepositoryEventsAsync(
        long repositoryId,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
