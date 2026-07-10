using Microsoft.Extensions.Configuration;

namespace GitCandy.Configuration;

internal static class GitCandyApplicationOptionsConfiguration
{
    public static void ApplyLegacyAliases(
        GitCandyApplicationOptions options,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(GitCandyApplicationOptions.SectionName);

        if (string.IsNullOrWhiteSpace(section[nameof(GitCandyApplicationOptions.UserConfigurationPath)]))
        {
            var legacyUserConfigurationPath = FirstValue(
                section[GitCandyApplicationOptions.LegacyUserConfigurationKey],
                configuration[GitCandyApplicationOptions.LegacyUserConfigurationKey]);

            if (!string.IsNullOrWhiteSpace(legacyUserConfigurationPath))
            {
                options.UserConfigurationPath = legacyUserConfigurationPath;
            }
        }
    }

    private static string? FirstValue(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
