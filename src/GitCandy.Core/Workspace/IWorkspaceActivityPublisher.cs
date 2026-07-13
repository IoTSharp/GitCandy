namespace GitCandy.Workspace;

/// <summary>由统一 transport 和后续业务切片发布版本化活动事件。</summary>
public interface IWorkspaceActivityPublisher
{
    Task PublishPushAsync(string repositoryStorageName, string actorName, string repositoryStateId, CancellationToken cancellationToken = default);
}
