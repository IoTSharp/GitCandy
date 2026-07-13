using System.Security.Cryptography;
using System.Text;
using GitCandy.Git;
using GitCandy.Integrations;
using GitCandy.Workspace;

namespace GitCandy.Schedules;

/// <summary>把 HTTP/SSH 统一 backend 的成功 push 投影为用户 Feed 事件。</summary>
public sealed class GitTransportWorkspaceActivitySink(
    IServiceScopeFactory serviceScopeFactory,
    IManagedGitRepositoryService repositoryService) : IGitTransportActivitySink
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly IManagedGitRepositoryService _repositoryService = repositoryService;

    public async Task OnCompletedAsync(GitTransportRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Service != GitTransportService.ReceivePack || request.AdvertiseRefs) return;
        var snapshot = _repositoryService.ReadSnapshot(request.Repository, cancellationToken);
        var references = new List<IntegrationReference>();
        foreach (var reference in snapshot.Branches.Concat(snapshot.Tags)
            .OrderBy(item => item.CanonicalName, StringComparer.Ordinal))
        {
            if (reference.TargetId is string targetSha)
            {
                references.Add(new IntegrationReference(reference.CanonicalName, targetSha));
            }
        }
        var stateText = string.Join('\n', references.Select(item => $"{item.Name}={item.TargetSha}"));
        var stateId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stateText)));
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        await Task.WhenAll(
            scope.ServiceProvider.GetRequiredService<IWorkspaceActivityPublisher>()
                .PublishPushAsync(request.Repository.RepositoryName, request.ActorName, stateId, cancellationToken),
            scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
                .PublishPushAsync(
                    request.Repository.RepositoryName,
                    request.ActorName,
                    stateId,
                    references,
                    cancellationToken));
    }
}
