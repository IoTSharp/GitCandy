using System.IO.Compression;
using System.Text;
using GitCandy.Configuration;
using LibGit2Sharp;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GitCandy.Git;

/// <summary>
/// 基于 LibGit2Sharp 的受限仓库读取服务。
/// </summary>
public sealed class RepositoryBrowserService(
    IManagedGitRepositoryService repositoryService,
    IOptions<RepositoryBrowserOptions> options,
    IMemoryCache memoryCache) : IRepositoryBrowserService
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly IManagedGitRepositoryService _repositoryService = repositoryService;
    private readonly RepositoryBrowserOptions _options = options.Value;
    private readonly IMemoryCache _memoryCache = memoryCache;

    /// <inheritdoc />
    public IReadOnlyList<RepositoryBranchSummary> ReadBranches(GitRepositoryContext repository, CancellationToken cancellationToken = default)
    {
        using var git = Open(repository);
        var head = git.Head;
        return git.Branches.Where(static branch => !branch.IsRemote && branch.Tip is not null)
            .Select(branch =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var divergence = head.Tip is null ? null : git.ObjectDatabase.CalculateHistoryDivergence(head.Tip, branch.Tip);
                return new RepositoryBranchSummary(branch.FriendlyName, branch.Tip.Id.Sha, branch.Tip.MessageShort, branch.Tip.Committer.When,
                    divergence?.AheadBy ?? 0, divergence?.BehindBy ?? 0,
                    string.Equals(branch.CanonicalName, head.CanonicalName, StringComparison.Ordinal));
            })
            .OrderByDescending(static branch => branch.IsDefault)
            .ThenBy(static branch => branch.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<RepositoryTagSummary> ReadTags(GitRepositoryContext repository, CancellationToken cancellationToken = default)
    {
        using var git = Open(repository);
        return git.Tags.Select(tag =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var annotation = tag.Annotation;
                var commit = tag.Target.Peel<Commit>();
                return commit is null ? null : new RepositoryTagSummary(tag.FriendlyName, commit.Id.Sha, annotation is not null,
                    annotation?.Tagger.Name, annotation?.Tagger.When, annotation?.Message?.Trim());
            })
            .Where(static tag => tag is not null)
            .Select(static tag => tag!)
            .OrderBy(static tag => tag.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public RepositoryStatisticsResult? ReadStatistics(GitRepositoryContext repository, string? revision, CancellationToken cancellationToken = default)
    {
        using var git = Open(repository);
        var resolved = ResolveRevision(git, revision);
        var commit = resolved is null ? null : git.Lookup<Commit>(resolved.CommitId);
        if (resolved is null || commit is null)
        {
            return null;
        }

        var cacheKey = $"repository-statistics:{repository.RepositoryName}:{resolved.CommitId}";
        if (_memoryCache.TryGetValue(cacheKey, out RepositoryStatisticsResult? cached) && cached is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return cached;
        }

        var commits = git.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = commit, SortBy = CommitSortStrategies.Time | CommitSortStrategies.Topological })
            .Take(_options.MaxStatisticsCommits + 1).ToArray();
        var truncated = commits.Length > _options.MaxStatisticsCommits;
        var boundedCommits = commits.Take(_options.MaxStatisticsCommits).ToArray();
        var contributors = boundedCommits
            .GroupBy(item => $"{item.Author.Name.Trim().ToUpperInvariant()}\n{item.Author.Email.Trim().ToUpperInvariant()}", StringComparer.Ordinal)
            .Select(group => new RepositoryContributorSummary(group.First().Author.Name, group.Count()))
            .OrderByDescending(static contributor => contributor.CommitCount)
            .ThenBy(static contributor => contributor.Name, StringComparer.OrdinalIgnoreCase)
            .Take(_options.MaxContributors).ToArray();
        long fileCount = 0;
        long sourceBytes = 0;
        CountTree(commit.Tree, ref fileCount, ref sourceBytes, cancellationToken);
        var repositoryBytes = Directory.EnumerateFiles(_repositoryService.ResolveExistingPath(repository), "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
        var result = new RepositoryStatisticsResult(resolved, boundedCommits.Length,
            boundedCommits.Select(item => $"{item.Author.Name.Trim().ToUpperInvariant()}\n{item.Author.Email.Trim().ToUpperInvariant()}").Distinct(StringComparer.Ordinal).Count(),
            fileCount, sourceBytes, repositoryBytes, truncated, contributors);
        _memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
        return result;
    }

    private static void CountTree(Tree tree, ref long fileCount, ref long sourceBytes, CancellationToken cancellationToken)
    {
        foreach (var entry in tree)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Target is Tree child) CountTree(child, ref fileCount, ref sourceBytes, cancellationToken);
            else if (entry.Target is Blob blob) { fileCount++; sourceBytes += blob.Size; }
        }
    }

    /// <inheritdoc />
    public RepositoryTreeResult? ReadTree(
        GitRepositoryContext repository,
        string? revision,
        string? path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = NormalizeGitPath(path, allowEmpty: true);
        using var git = Open(repository);
        var resolved = ResolveRevision(git, revision);
        if (resolved is null)
        {
            return null;
        }

        var commit = git.Lookup<Commit>(resolved.CommitId);
        var tree = string.IsNullOrEmpty(normalizedPath)
            ? commit?.Tree
            : commit?[normalizedPath]?.Target as Tree;
        if (tree is null)
        {
            return null;
        }

        var entries = tree
            .Select(entry => ToTreeEntry(normalizedPath, entry))
            .OrderBy(static entry => entry.Kind == RepositoryTreeEntryKind.Tree ? 0 : 1)
            .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        return new RepositoryTreeResult(
            resolved,
            normalizedPath,
            entries,
            ReadBranches(git),
            ReadTags(git));
    }

    /// <inheritdoc />
    public RepositoryBlobResult? ReadBlob(
        GitRepositoryContext repository,
        string? revision,
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = NormalizeGitPath(path, allowEmpty: false);
        using var git = Open(repository);
        var resolved = ResolveRevision(git, revision);
        var blob = ResolveBlob(git, resolved, normalizedPath);
        if (resolved is null || blob is null)
        {
            return null;
        }

        return ReadBlobResult(resolved, normalizedPath, blob, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RepositoryBlobResult?> CopyBlobAsync(
        GitRepositoryContext repository,
        string? revision,
        string path,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var normalizedPath = NormalizeGitPath(path, allowEmpty: false);
        using var git = Open(repository);
        var resolved = ResolveRevision(git, revision);
        var blob = ResolveBlob(git, resolved, normalizedPath);
        if (resolved is null || blob is null)
        {
            return null;
        }

        var result = ReadBlobMetadata(resolved, normalizedPath, blob);
        await using var input = blob.GetContentStream();
        await input.CopyToAsync(output, 128 * 1024, cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public RepositoryCommitPage? ReadCommits(
        GitRepositoryContext repository,
        string? revision,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        using var git = Open(repository);
        var resolved = ResolveRevision(git, revision);
        if (resolved is null)
        {
            return null;
        }

        var commits = git.Commits.QueryBy(new CommitFilter
            {
                IncludeReachableFrom = resolved.CommitId,
                SortBy = CommitSortStrategies.Time | CommitSortStrategies.Topological
            })
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .Select(ToCommitSummary)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var hasNextPage = commits.Length > pageSize;
        return new RepositoryCommitPage(
            resolved,
            page,
            pageSize,
            hasNextPage,
            commits.Take(pageSize).ToArray());
    }

    /// <inheritdoc />
    public RepositoryCommitResult? ReadCommit(
        GitRepositoryContext repository,
        string commitId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitId);
        using var git = Open(repository);
        var commit = git.Lookup<Commit>(commitId.Trim());
        if (commit is null)
        {
            return null;
        }

        var parent = commit.Parents.FirstOrDefault();
        var diff = ReadDiff(git, parent?.Tree, commit.Tree, cancellationToken);
        return new RepositoryCommitResult(ToCommitSummary(commit), diff.Files, diff.Truncated);
    }

    /// <inheritdoc />
    public RepositoryBlameResult? ReadBlame(
        GitRepositoryContext repository,
        string? revision,
        string path,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizeGitPath(path, allowEmpty: false);
        using var git = Open(repository);
        var resolved = ResolveRevision(git, revision);
        var blob = ResolveBlob(git, resolved, normalizedPath);
        if (resolved is null || blob is null || blob.Size > _options.MaxDisplayedBlobBytes)
        {
            return null;
        }

        var blobResult = ReadBlobResult(resolved, normalizedPath, blob, cancellationToken);
        if (blobResult.IsBinary || blobResult.HasUnknownEncoding || blobResult.Text is null)
        {
            return null;
        }

        var lines = SplitLines(blobResult.Text);
        var hunks = new List<RepositoryBlameHunk>();
        foreach (var hunk in git.Blame(normalizedPath, new BlameOptions { StartingAt = resolved.CommitId }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commit = hunk.FinalCommit;
            var startLine = checked(hunk.FinalStartLineNumber + 1);
            hunks.Add(new RepositoryBlameHunk(
                commit.Id.Sha,
                commit.Author.Name,
                commit.Author.Email,
                commit.Author.When,
                startLine,
                lines.Skip(startLine - 1).Take(hunk.LineCount).ToArray()));
        }

        return new RepositoryBlameResult(resolved, normalizedPath, hunks);
    }

    /// <inheritdoc />
    public RepositoryCompareResult? Compare(
        GitRepositoryContext repository,
        string baseRevision,
        string headRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(headRevision);
        using var git = Open(repository);
        var baseResolved = ResolveRevision(git, baseRevision);
        var headResolved = ResolveRevision(git, headRevision);
        if (baseResolved is null || headResolved is null)
        {
            return null;
        }

        var baseCommit = git.Lookup<Commit>(baseResolved.CommitId);
        var headCommit = git.Lookup<Commit>(headResolved.CommitId);
        if (baseCommit is null || headCommit is null)
        {
            return null;
        }

        var divergence = git.ObjectDatabase.CalculateHistoryDivergence(baseCommit, headCommit);
        var commits = git.Commits.QueryBy(new CommitFilter
            {
                IncludeReachableFrom = headCommit,
                ExcludeReachableFrom = baseCommit,
                SortBy = CommitSortStrategies.Time | CommitSortStrategies.Topological
            })
            .Take(500)
            .Select(ToCommitSummary)
            .ToArray();
        var diff = ReadDiff(git, baseCommit.Tree, headCommit.Tree, cancellationToken);
        return new RepositoryCompareResult(
            baseResolved,
            headResolved,
            divergence?.BehindBy ?? commits.Length,
            divergence?.AheadBy ?? 0,
            commits,
            diff.Files,
            diff.Truncated);
    }

    /// <inheritdoc />
    public async Task<RepositoryRevision?> WriteArchiveAsync(
        GitRepositoryContext repository,
        string? revision,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        using var git = Open(repository);
        var resolved = ResolveRevision(git, revision);
        var commit = resolved is null ? null : git.Lookup<Commit>(resolved.CommitId);
        if (resolved is null || commit is null)
        {
            return null;
        }

        var entries = EnumerateArchiveEntries(commit.Tree, string.Empty, cancellationToken).ToArray();
        if (entries.Length > _options.MaxArchiveEntries
            || entries.Sum(static item => item.Blob?.Size ?? 0) > _options.MaxArchiveBytes)
        {
            throw new InvalidOperationException("The requested archive exceeds the configured resource limits.");
        }

        await using var archiveOutput = new AsyncZipWriteStream(output);
        using (var archive = new ZipArchive(archiveOutput, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry(item.Path, CompressionLevel.Fastest);
                if (item.Blob is null)
                {
                    continue;
                }

                await using var source = item.Blob.GetContentStream();
                await using var destination = entry.Open();
                await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
            }
        }

        await archiveOutput.FlushAsync(cancellationToken);

        return resolved;
    }

    private Repository Open(GitRepositoryContext repository)
    {
        return new Repository(_repositoryService.ResolveExistingPath(repository));
    }

    private static RepositoryRevision? ResolveRevision(Repository repository, string? revision)
    {
        var requested = string.IsNullOrWhiteSpace(revision) ? "HEAD" : revision.Trim();
        var commit = repository.Lookup<Commit>(requested);
        if (commit is null)
        {
            var target = repository.Lookup(requested);
            commit = target switch
            {
                Commit directCommit => directCommit,
                TagAnnotation annotation => annotation.Target.Peel<Commit>(),
                _ => target?.Peel<Commit>()
            };
        }

        return commit is null
            ? null
            : new RepositoryRevision(requested, commit.Id.Sha, requested);
    }

    private static Blob? ResolveBlob(
        Repository repository,
        RepositoryRevision? revision,
        string path)
    {
        var commit = revision is null ? null : repository.Lookup<Commit>(revision.CommitId);
        return commit?[path]?.Target as Blob;
    }

    private RepositoryBlobResult ReadBlobResult(
        RepositoryRevision revision,
        string path,
        Blob blob,
        CancellationToken cancellationToken)
    {
        var metadata = ReadBlobMetadata(revision, path, blob);
        if (metadata.IsTooLarge)
        {
            return metadata;
        }

        using var stream = blob.GetContentStream();
        using var memory = new MemoryStream(checked((int)blob.Size));
        stream.CopyTo(memory);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = memory.ToArray();
        if (IsBinary(bytes))
        {
            return metadata with { IsBinary = true };
        }

        var (text, unknownEncoding) = DecodeText(bytes);
        return metadata with
        {
            HasUnknownEncoding = unknownEncoding,
            Text = text,
            LineCount = text is null ? 0 : SplitLines(text).Length
        };
    }

    private RepositoryBlobResult ReadBlobMetadata(
        RepositoryRevision revision,
        string path,
        Blob blob)
    {
        return new RepositoryBlobResult(
            revision,
            path,
            Path.GetFileName(path),
            blob.Id.Sha,
            blob.Size,
            IsBinary: false,
            IsTooLarge: blob.Size > _options.MaxDisplayedBlobBytes,
            HasUnknownEncoding: false,
            Text: null,
            Language: DetectLanguage(path),
            LineCount: 0);
    }

    private (IReadOnlyList<RepositoryDiffFile> Files, bool Truncated) ReadDiff(
        Repository repository,
        Tree? oldTree,
        Tree newTree,
        CancellationToken cancellationToken)
    {
        var patch = repository.Diff.Compare<Patch>(oldTree, newTree);
        var files = new List<RepositoryDiffFile>();
        var characters = 0;
        var truncated = false;
        foreach (var change in patch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.Count >= _options.MaxDiffFiles)
            {
                truncated = true;
                break;
            }

            var content = change.Patch;
            if (characters + content.Length > _options.MaxDiffCharacters)
            {
                content = null;
                truncated = true;
            }
            else
            {
                characters += content.Length;
            }

            files.Add(new RepositoryDiffFile(
                change.Path,
                change.OldPath,
                change.Status.ToString(),
                change.IsBinaryComparison,
                change.LinesAdded,
                change.LinesDeleted,
                content));
        }

        return (files, truncated);
    }

    private static RepositoryTreeEntry ToTreeEntry(string parentPath, TreeEntry entry)
    {
        var path = string.IsNullOrEmpty(parentPath) ? entry.Name : $"{parentPath}/{entry.Name}";
        var kind = entry.TargetType switch
        {
            TreeEntryTargetType.Tree => RepositoryTreeEntryKind.Tree,
            TreeEntryTargetType.GitLink => RepositoryTreeEntryKind.Submodule,
            _ when entry.Mode == Mode.SymbolicLink => RepositoryTreeEntryKind.Symlink,
            _ => RepositoryTreeEntryKind.Blob
        };
        return new RepositoryTreeEntry(
            entry.Name,
            path,
            kind,
            entry.Target.Id.Sha,
            entry.Target is Blob blob ? blob.Size : null);
    }

    private static RepositoryCommitSummary ToCommitSummary(Commit commit)
    {
        return new RepositoryCommitSummary(
            commit.Id.Sha,
            commit.Message,
            commit.MessageShort,
            commit.Author.Name,
            commit.Author.Email,
            commit.Author.When,
            commit.Parents.Select(static parent => parent.Id.Sha).ToArray());
    }

    private static IReadOnlyList<GitReferenceSnapshot> ReadBranches(Repository repository)
    {
        return repository.Branches
            .Where(static branch => !branch.IsRemote)
            .Select(static branch => new GitReferenceSnapshot(branch.FriendlyName, branch.Tip?.Id.Sha))
            .OrderBy(static branch => branch.CanonicalName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<GitReferenceSnapshot> ReadTags(Repository repository)
    {
        return repository.Tags
            .Select(static tag => new GitReferenceSnapshot(tag.FriendlyName, tag.Target.Peel<Commit>()?.Id.Sha))
            .OrderBy(static tag => tag.CanonicalName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<ArchiveEntrySource> EnumerateArchiveEntries(
        Tree tree,
        string parentPath,
        CancellationToken cancellationToken)
    {
        foreach (var entry in tree)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = string.IsNullOrEmpty(parentPath) ? entry.Name : $"{parentPath}/{entry.Name}";
            if (entry.Target is Tree childTree)
            {
                foreach (var child in EnumerateArchiveEntries(childTree, path, cancellationToken))
                {
                    yield return child;
                }
            }
            else if (entry.Target is Blob blob)
            {
                yield return new ArchiveEntrySource(path, blob);
            }
            else if (entry.TargetType == TreeEntryTargetType.GitLink)
            {
                yield return new ArchiveEntrySource($"{path}/", null);
            }
        }
    }

    private static string NormalizeGitPath(string? path, bool allowEmpty)
    {
        var value = path?.Trim('/') ?? string.Empty;
        if (value.Length == 0)
        {
            return allowEmpty
                ? string.Empty
                : throw new ArgumentException("A Git path is required.", nameof(path));
        }

        if (value.Contains('\\', StringComparison.Ordinal)
            || value.Split('/').Any(static segment => segment.Length == 0 || segment is "." or "..")
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("The Git path is not canonical.", nameof(path));
        }

        return value;
    }

    private static bool IsBinary(ReadOnlySpan<byte> bytes)
    {
        return bytes[..Math.Min(bytes.Length, 8 * 1024)].Contains((byte)0);
    }

    private static (string? Text, bool UnknownEncoding) DecodeText(byte[] bytes)
    {
        try
        {
            if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
            {
                return (Encoding.Unicode.GetString(bytes.AsSpan(2)), false);
            }

            if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
            {
                return (Encoding.BigEndianUnicode.GetString(bytes.AsSpan(2)), false);
            }

            if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            {
                return (StrictUtf8.GetString(bytes.AsSpan(3)), false);
            }

            return (StrictUtf8.GetString(bytes), false);
        }
        catch (DecoderFallbackException)
        {
            return (null, true);
        }
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static string DetectLanguage(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".cshtml" => "razor",
            ".js" or ".mjs" => "javascript",
            ".ts" or ".tsx" => "typescript",
            ".json" => "json",
            ".xml" or ".csproj" or ".props" or ".targets" => "xml",
            ".html" or ".htm" => "html",
            ".css" => "css",
            ".md" => "markdown",
            ".sh" or ".bash" => "bash",
            ".ps1" => "powershell",
            ".sql" => "sql",
            ".py" => "python",
            ".go" => "go",
            ".rs" => "rust",
            ".yml" or ".yaml" => "yaml",
            _ => "plaintext"
        };
    }

    private sealed record ArchiveEntrySource(string Path, Blob? Blob);

    private sealed class AsyncZipWriteStream(Stream output) : Stream
    {
        private readonly Stream _output = output;
        private readonly MemoryStream _pending = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await FlushPendingAsync(cancellationToken);
            await _output.FlushAsync(cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _pending.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _pending.Write(buffer);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await FlushPendingAsync(cancellationToken);
            await _output.WriteAsync(buffer, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pending.Dispose();
            }

            base.Dispose(disposing);
        }

        private async Task FlushPendingAsync(CancellationToken cancellationToken)
        {
            if (_pending.Length == 0)
            {
                return;
            }

            _pending.Position = 0;
            await _pending.CopyToAsync(_output, 16 * 1024, cancellationToken);
            _pending.SetLength(0);
        }
    }
}
