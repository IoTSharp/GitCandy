using System.Text.Json;
using GitCandy.PullRequests;
using LibGit2Sharp;

namespace GitCandy.Git;

public sealed partial class PullRequestGitRepository
{
    private const int ReviewAnchorContextLimit = 8192;

    /// <inheritdoc />
    public PullRequestReviewAnchor? CaptureReviewAnchor(
        string repositoryStorageName, string baseSha, string headSha, string path,
        PullRequestDiffSide side, int startLine, int endLine, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (startLine < 1 || endLine < startLine) return null;
        using var repository = Open(repositoryStorageName);
        var patch = ReadReviewPatch(repository, baseSha, headSha);
        if (patch is null) return null;

        foreach (var change in patch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidatePath = PathForSide(change, side);
            if (change.IsBinaryComparison || !string.Equals(candidatePath, path, StringComparison.Ordinal)) continue;
            var sideLines = LinesForSide(ParsePatchLines(change.Patch), side);
            var selected = sideLines.Where(line => LineNumber(line, side) is int number && number >= startLine && number <= endLine).ToArray();
            if (selected.Length != endLine - startLine + 1) return null;
            var firstIndex = sideLines.IndexOf(selected[0]);
            var lastIndex = firstIndex + selected.Length - 1;
            var context = new StoredReviewContext(
                sideLines.Skip(Math.Max(0, firstIndex - 3)).Take(Math.Min(3, firstIndex)).Select(static line => line.Content).ToArray(),
                selected.Select(static line => line.Content).ToArray(),
                sideLines.Skip(lastIndex + 1).Take(3).Select(static line => line.Content).ToArray());
            var serializedContext = JsonSerializer.Serialize(context);
            return serializedContext.Length <= ReviewAnchorContextLimit
                ? new PullRequestReviewAnchor(candidatePath, side, startLine, endLine, serializedContext)
                : null;
        }

        return null;
    }

    /// <inheritdoc />
    public PullRequestReviewAnchor? RemapReviewAnchor(
        string repositoryStorageName, string baseSha, string headSha, PullRequestDiffSide side,
        string context, CancellationToken cancellationToken = default)
    {
        StoredReviewContext? stored;
        try { stored = JsonSerializer.Deserialize<StoredReviewContext>(context); }
        catch (JsonException) { return null; }
        if (stored is null || stored.Selected.Length == 0) return null;
        using var repository = Open(repositoryStorageName);
        var patch = ReadReviewPatch(repository, baseSha, headSha);
        if (patch is null) return null;

        PullRequestReviewAnchor? match = null;
        foreach (var change in patch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (change.IsBinaryComparison) continue;
            var sideLines = LinesForSide(ParsePatchLines(change.Patch), side);
            for (var index = 0; index + stored.Selected.Length <= sideLines.Count; index++)
            {
                var selected = sideLines.Skip(index).Take(stored.Selected.Length).ToArray();
                if (!selected.Select(static line => line.Content).SequenceEqual(stored.Selected, StringComparer.Ordinal)
                    || !ContextMatches(sideLines, index, selected.Length, stored)) continue;
                var start = LineNumber(selected[0], side);
                var end = LineNumber(selected[^1], side);
                if (start is null || end is null) continue;
                var candidate = new PullRequestReviewAnchor(PathForSide(change, side), side, start.Value, end.Value, context);
                if (match is not null) return null;
                match = candidate;
            }
        }

        return match;
    }

    private static Patch? ReadReviewPatch(Repository repository, string baseSha, string headSha)
    {
        var baseCommit = repository.Lookup<Commit>(baseSha.Trim());
        var headCommit = repository.Lookup<Commit>(headSha.Trim());
        if (baseCommit is null || headCommit is null) return null;
        var mergeBase = repository.ObjectDatabase.FindMergeBase(baseCommit, headCommit);
        return mergeBase is null ? null : repository.Diff.Compare<Patch>(mergeBase.Tree, headCommit.Tree, new CompareOptions { Similarity = SimilarityOptions.Renames });
    }

    private static List<PatchLine> ParsePatchLines(string patch)
    {
        var result = new List<PatchLine>();
        var oldLine = 0;
        var newLine = 0;
        var inHunk = false;
        foreach (var line in patch.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                inHunk = TryParseHunkStart(line, out oldLine, out newLine);
                continue;
            }

            if (!inHunk || line.Length == 0 || line[0] is not (' ' or '+' or '-')) continue;
            var origin = line[0];
            result.Add(new PatchLine(origin == '+' ? null : oldLine, origin == '-' ? null : newLine, line[1..]));
            if (origin != '+') oldLine++;
            if (origin != '-') newLine++;
        }

        return result;
    }

    private static bool TryParseHunkStart(string header, out int oldLine, out int newLine)
    {
        oldLine = 0;
        newLine = 0;
        var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3 && TryParseRangeStart(parts[1], '-', out oldLine) && TryParseRangeStart(parts[2], '+', out newLine);
    }

    private static bool TryParseRangeStart(string range, char prefix, out int value)
    {
        value = 0;
        if (range.Length < 2 || range[0] != prefix) return false;
        var comma = range.IndexOf(',');
        return int.TryParse(range.AsSpan(1, comma < 0 ? range.Length - 1 : comma - 1), out value);
    }

    private static List<PatchLine> LinesForSide(IReadOnlyList<PatchLine> lines, PullRequestDiffSide side) =>
        lines.Where(line => LineNumber(line, side) is not null).ToList();

    private static int? LineNumber(PatchLine line, PullRequestDiffSide side) => side == PullRequestDiffSide.Old ? line.OldLine : line.NewLine;
    private static string PathForSide(PatchEntryChanges change, PullRequestDiffSide side) =>
        side == PullRequestDiffSide.Old && !string.IsNullOrEmpty(change.OldPath) ? change.OldPath : change.Path;

    private static bool ContextMatches(IReadOnlyList<PatchLine> lines, int selectedIndex, int selectedLength, StoredReviewContext stored)
    {
        if (selectedIndex < stored.Before.Length
            || !lines.Skip(selectedIndex - stored.Before.Length).Take(stored.Before.Length).Select(static line => line.Content).SequenceEqual(stored.Before, StringComparer.Ordinal)) return false;
        var afterStart = selectedIndex + selectedLength;
        return lines.Count - afterStart >= stored.After.Length
            && lines.Skip(afterStart).Take(stored.After.Length).Select(static line => line.Content).SequenceEqual(stored.After, StringComparer.Ordinal);
    }

    private sealed record StoredReviewContext(string[] Before, string[] Selected, string[] After);
    private sealed record PatchLine(int? OldLine, int? NewLine, string Content);
}
