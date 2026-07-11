using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Ganss.Xss;
using GitCandy.Issues;
using Markdig;

namespace GitCandy.Application;

/// <summary>以 Markdig 解析受限 CommonMark，并以白名单清洗最终 HTML。</summary>
internal sealed partial class IssueMarkdownRenderer : IIssueMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseTaskLists()
        .Build();

    private readonly HtmlSanitizer _sanitizer = CreateSanitizer();

    public string Render(string markdown, string namespaceSlug, string repositorySlug)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var expanded = ExpandReferences(markdown, namespaceSlug, repositorySlug);
        return _sanitizer.Sanitize(Markdown.ToHtml(expanded, Pipeline));
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
        {
            "p", "br", "pre", "code", "blockquote", "ul", "ol", "li", "strong", "em",
            "del", "a", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "input"
        })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[] { "href", "title", "class", "type", "checked", "disabled" })
        {
            sanitizer.AllowedAttributes.Add(attribute);
        }

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");
        return sanitizer;
    }

    private static string ExpandReferences(string markdown, string namespaceSlug, string repositorySlug)
    {
        var result = new StringBuilder(markdown.Length + 64);
        var inFence = false;
        foreach (var line in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal)
                || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                result.AppendLine(line);
                continue;
            }

            result.AppendLine(inFence ? line : ExpandInline(line, namespaceSlug, repositorySlug));
        }

        return result.ToString();
    }

    private static string ExpandInline(string line, string namespaceSlug, string repositorySlug)
    {
        var segments = InlineCodeRegex().Split(line);
        for (var index = 0; index < segments.Length; index += 2)
        {
            var value = CrossRepositoryIssueRegex().Replace(segments[index], match =>
            {
                var owner = Uri.EscapeDataString(match.Groups[1].Value);
                var repository = Uri.EscapeDataString(match.Groups[2].Value);
                var number = match.Groups[3].Value;
                return $"[{WebUtility.HtmlEncode(match.Value)}](/{owner}/{repository}/issues/{number})";
            });
            value = LocalIssueRegex().Replace(value, match =>
                $"[#{match.Groups[1].Value}](/{Uri.EscapeDataString(namespaceSlug)}/{Uri.EscapeDataString(repositorySlug)}/issues/{match.Groups[1].Value})");
            value = MentionRegex().Replace(value, match =>
                $"[@{match.Groups[1].Value}](/Account/Detail/{Uri.EscapeDataString(match.Groups[1].Value)})");
            segments[index] = value;
        }

        return string.Concat(segments);
    }

    [GeneratedRegex("(`+[^`]*`+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"(?<![\w/])([A-Za-z0-9][A-Za-z0-9_.-]{0,49})/([A-Za-z0-9][A-Za-z0-9_.-]{0,49})#([1-9][0-9]*)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CrossRepositoryIssueRegex();

    [GeneratedRegex(@"(?<![\w/])#([1-9][0-9]*)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LocalIssueRegex();

    [GeneratedRegex(@"(?<![\w])@([A-Za-z0-9][A-Za-z0-9_.-]{0,63})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex MentionRegex();
}
