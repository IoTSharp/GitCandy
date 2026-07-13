namespace GitCandy.Git;

/// <summary>成功 Git transport 操作的非阻断活动观察器。</summary>
public interface IGitTransportActivitySink
{
    Task OnCompletedAsync(GitTransportRequest request, CancellationToken cancellationToken = default);
}
