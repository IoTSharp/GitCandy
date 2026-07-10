using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using GitCandy.Git;

namespace GitCandy.Ssh;

internal static partial class GitSshCommandParser
{
    [GeneratedRegex(
        "^(?<command>git-upload-pack|git-receive-pack|git-upload-archive) '/?git/(?<repository>[^/\\\\'\\r\\n]+)\\.git'$",
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
        parsedCommand = new ParsedGitCommand(service, match.Groups["repository"].Value);
        return true;
    }
}

internal sealed record ParsedGitCommand(GitTransportService Service, string RepositoryName);
