using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using GitCandy.Application;
using GitCandy.Git;

namespace GitCandy.Ssh;

internal static partial class GitSshCommandParser
{
    [GeneratedRegex(
        "^(?<command>git-upload-pack|git-receive-pack|git-upload-archive) '/?(?<path>[^'\\r\\n]+)'$",
        RegexOptions.CultureInvariant,
        1000)]
    private static partial Regex GitCommandPattern();

    public static bool TryParse(
        string? commandText,
        [NotNullWhen(true)] out ParsedGitCommand? parsedCommand)
    {
        var match = GitCommandPattern().Match(commandText ?? string.Empty);
        if (!match.Success)
        {
            parsedCommand = null;
            return false;
        }

        var service = match.Groups["command"].Value switch
        {
            "git-upload-pack" => GitTransportService.UploadPack,
            "git-receive-pack" => GitTransportService.ReceivePack,
            "git-upload-archive" => GitTransportService.UploadArchive,
            _ => throw new InvalidOperationException("The SSH Git command was not recognized.")
        };
        var path = match.Groups["path"].Value.Trim('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            parsedCommand = null;
            return false;
        }

        if (!segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            parsedCommand = null;
            return false;
        }

        var repositorySlug = StripDotGit(segments[1]);
        if (!NamespaceSlugRules.IsValidNamespaceSlug(segments[0])
            || !NamespaceSlugRules.IsValidRepositorySlug(repositorySlug))
        {
            parsedCommand = null;
            return false;
        }

        parsedCommand = new ParsedGitCommand(service, segments[0], repositorySlug);
        return true;
    }

    private static string StripDotGit(string value)
    {
        return value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? value[..^4]
            : value;
    }
}

internal sealed record ParsedGitCommand(
    GitTransportService Service,
    string NamespaceSlug,
    string RepositorySlug);
