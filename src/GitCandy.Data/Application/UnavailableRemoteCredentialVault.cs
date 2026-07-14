using GitCandy.Remotes;

namespace GitCandy.Application;

internal sealed class UnavailableRemoteCredentialVault : IRemoteCredentialVault
{
    private const string NotConfiguredMessage = "A remote credential vault has not been configured.";

    public Task<RemoteCredentialMetadata> StoreAsync(
        RemoteConnectionOwner owner,
        RemoteCredential credential,
        CancellationToken cancellationToken = default) =>
        Task.FromException<RemoteCredentialMetadata>(new InvalidOperationException(NotConfiguredMessage));

    public ValueTask<RemoteCredential?> ResolveAsync(
        RemoteSecretReference reference,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<RemoteCredential?>(null);

    public Task<RemoteCredentialMetadata?> RotateAsync(
        RemoteSecretReference reference,
        RemoteCredential replacement,
        CancellationToken cancellationToken = default) =>
        Task.FromException<RemoteCredentialMetadata?>(new InvalidOperationException(NotConfiguredMessage));

    public Task<bool> RevokeAsync(
        RemoteSecretReference reference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
