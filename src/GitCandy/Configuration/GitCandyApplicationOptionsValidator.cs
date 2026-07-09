using System.Globalization;
using Microsoft.Extensions.Options;

namespace GitCandy.Configuration;

/// <summary>
/// GitCandy 应用级配置验证器。
/// </summary>
public sealed class GitCandyApplicationOptionsValidator : IValidateOptions<GitCandyApplicationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GitCandyApplicationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        ValidateRequired(options.LogPathFormat, nameof(options.LogPathFormat), failures);
        ValidateLogPathFormat(options.LogPathFormat, failures);
        ValidateRequired(options.UserConfigurationPath, nameof(options.UserConfigurationPath), failures);
        ValidateRequired(options.RepositoryPath, nameof(options.RepositoryPath), failures);
        ValidateRequired(options.CachePath, nameof(options.CachePath), failures);

        ValidatePort(options.SslPort, nameof(options.SslPort), failures);
        ValidatePort(options.SshPort, nameof(options.SshPort), failures);
        ValidatePositive(options.NumberOfCommitsPerPage, nameof(options.NumberOfCommitsPerPage), failures);
        ValidatePositive(options.NumberOfItemsPerList, nameof(options.NumberOfItemsPerList), failures);
        ValidatePositive(options.NumberOfRepositoryContributors, nameof(options.NumberOfRepositoryContributors), failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRequired(string? value, string optionName, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{optionName} must be configured.");
        }
    }

    private static void ValidatePositive(int value, string optionName, List<string> failures)
    {
        if (value <= 0)
        {
            failures.Add($"{optionName} must be greater than zero.");
        }
    }

    private static void ValidatePort(int value, string optionName, List<string> failures)
    {
        if (value is < 1 or > 65535)
        {
            failures.Add($"{optionName} must be between 1 and 65535.");
        }
    }

    private static void ValidateLogPathFormat(string? value, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, value, "yyyyMMdd");
        }
        catch (FormatException)
        {
            failures.Add($"{nameof(GitCandyApplicationOptions.LogPathFormat)} must be a valid composite format string.");
        }
    }
}
