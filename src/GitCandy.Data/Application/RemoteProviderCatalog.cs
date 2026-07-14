using GitCandy.Remotes;

namespace GitCandy.Application;

internal sealed class RemoteProviderCatalog : IRemoteProviderCatalog
{
    private readonly IReadOnlyDictionary<RemoteProviderKind, IRemoteRepositoryProvider> _providers;

    public RemoteProviderCatalog(IEnumerable<IRemoteRepositoryProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var providerMap = new Dictionary<RemoteProviderKind, IRemoteRepositoryProvider>();
        foreach (var provider in providers)
        {
            if (!providerMap.TryAdd(provider.Kind, provider))
            {
                throw new InvalidOperationException(
                    $"More than one remote repository provider is registered for '{provider.Kind}'.");
            }
        }

        _providers = providerMap;
        AvailableProviders = providerMap.Keys.Order().ToArray();
    }

    public IReadOnlyList<RemoteProviderKind> AvailableProviders { get; }

    public IRemoteRepositoryProvider? Get(RemoteProviderKind kind) =>
        _providers.TryGetValue(kind, out var provider) ? provider : null;
}
