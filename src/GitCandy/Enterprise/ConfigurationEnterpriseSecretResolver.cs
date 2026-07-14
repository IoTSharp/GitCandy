using GitCandy.Enterprise;

namespace GitCandy.Web.Enterprise;

internal sealed class ConfigurationEnterpriseSecretResolver(IConfiguration configuration)
    : IEnterpriseSecretResolver
{
    private readonly IConfiguration _configuration = configuration;

    public ValueTask<EnterpriseSecret?> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            return ValueTask.FromResult<EnterpriseSecret?>(null);
        }

        var separator = secretReference.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == secretReference.Length - 1)
        {
            return ValueTask.FromResult<EnterpriseSecret?>(null);
        }

        var scheme = secretReference[..separator];
        var key = secretReference[(separator + 1)..];
        string? value = scheme.ToUpperInvariant() switch
        {
            "ENV" when key.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '_') =>
                Environment.GetEnvironmentVariable(key),
            "CONFIG" when !key.Contains("..", StringComparison.Ordinal)
                && !key.Any(char.IsControl) => _configuration[key],
            _ => null
        };
        return ValueTask.FromResult(string.IsNullOrWhiteSpace(value)
            ? null
            : new EnterpriseSecret(value));
    }
}
