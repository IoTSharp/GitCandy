using GitCandy.Remotes;

namespace GitCandy.Web.Remotes;

internal sealed class ConfigurationRemoteSecretResolver(IConfiguration configuration)
    : IRemoteSecretResolver
{
    private readonly IConfiguration _configuration = configuration;

    public ValueTask<RemoteSecret?> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            return ValueTask.FromResult<RemoteSecret?>(null);
        }

        var separator = secretReference.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == secretReference.Length - 1)
        {
            return ValueTask.FromResult<RemoteSecret?>(null);
        }

        var scheme = secretReference[..separator];
        var key = secretReference[(separator + 1)..];
        string? value = scheme.ToUpperInvariant() switch
        {
            "ENV" when key.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character == '_') =>
                Environment.GetEnvironmentVariable(key),
            "CONFIG" when !key.Contains("..", StringComparison.Ordinal)
                && !key.Any(char.IsControl) => _configuration[key],
            _ => null
        };
        return ValueTask.FromResult(string.IsNullOrWhiteSpace(value)
            ? null
            : new RemoteSecret(value));
    }
}
