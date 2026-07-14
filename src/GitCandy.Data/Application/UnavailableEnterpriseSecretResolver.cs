using GitCandy.Enterprise;

namespace GitCandy.Application;

internal sealed class UnavailableEnterpriseSecretResolver : IEnterpriseSecretResolver
{
    public ValueTask<EnterpriseSecret?> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<EnterpriseSecret?>(null);
}
