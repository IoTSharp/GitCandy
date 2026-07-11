namespace GitCandy.Application;

/// <summary>为 Identity 用户和团队建立稳定 namespace。</summary>
public interface INamespaceProvisioningService
{
    /// <summary>确保指定用户具有稳定 namespace。</summary>
    Task<long?> EnsureUserNamespaceAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>确保指定团队具有稳定 namespace。</summary>
    Task<long?> EnsureTeamNamespaceAsync(
        long teamId,
        CancellationToken cancellationToken = default);
}
