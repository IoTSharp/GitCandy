using System.Text.RegularExpressions;
using GitCandy.Issues;

namespace GitCandy.Git;

/// <summary>从仓库默认 revision 的受控 Issue template 目录读取模板。</summary>
internal sealed partial class IssueTemplateService(
    IGitServiceFactory gitServiceFactory,
    IRepositoryBrowserService repositoryBrowserService) : IIssueTemplateService
{
    private const string TemplateRoot = ".gitcandy/ISSUE_TEMPLATE";
    private readonly IGitServiceFactory _gitServiceFactory = gitServiceFactory;
    private readonly IRepositoryBrowserService _repositoryBrowserService = repositoryBrowserService;

    public Task<IssueTemplate?> GetTemplateAsync(string repositoryStorageName, string? name, CancellationToken cancellationToken = default)
    {
        var templateName = string.IsNullOrWhiteSpace(name) ? "default" : name.Trim();
        if (!TemplateNameRegex().IsMatch(templateName))
        {
            return Task.FromResult<IssueTemplate?>(null);
        }

        try
        {
            var blob = _repositoryBrowserService.ReadBlob(
                _gitServiceFactory.Create(repositoryStorageName),
                revision: null,
                $"{TemplateRoot}/{templateName}.md",
                cancellationToken);
            if (blob is null || blob.IsBinary || blob.IsTooLarge || blob.Text is null || blob.Text.Length > 65536)
            {
                return Task.FromResult<IssueTemplate?>(null);
            }

            return Task.FromResult<IssueTemplate?>(Parse(templateName, blob.Text));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or GitRepositoryNotFoundException or LibGit2Sharp.LibGit2SharpException)
        {
            return Task.FromResult<IssueTemplate?>(null);
        }
    }

    private static IssueTemplate Parse(string name, string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return new IssueTemplate(name, string.Empty, normalized);
        }

        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            return new IssueTemplate(name, string.Empty, normalized);
        }

        var title = normalized[4..end].Split('\n')
            .Select(static line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(static parts => parts.Length == 2 && parts[0].Equals("title", StringComparison.OrdinalIgnoreCase))
            .Select(static parts => parts[1].Trim('"', '\''))
            .FirstOrDefault() ?? string.Empty;
        return new IssueTemplate(name, title, normalized[(end + 5)..]);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex TemplateNameRegex();
}
