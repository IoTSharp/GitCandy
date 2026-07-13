using System.Text;
using GitCandy.Search;
using LibGit2Sharp;

namespace GitCandy.Git;

/// <summary>对权限过滤后的仓库执行有界 commit 与默认分支代码搜索。</summary>
public sealed class GitContentSearchService(
    IGitRepositoryPathResolver pathResolver,
    IManagedGitRepositoryService repositoryService) : IGitContentSearchService
{
    private const int MaxRepositories = 20;
    private const int MaxCommitsPerRepository = 200;
    private const int MaxFilesPerRepository = 500;
    private const long MaxCodeFileBytes = 256 * 1024;

    public IReadOnlyList<SearchHit> Search(
        IReadOnlyList<SearchRepositoryCandidate> repositories,
        string query,
        SearchScope scope,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        var text = query.Trim();
        if (text.Length is < 2 or > 200 || limit <= 0) return [];
        var hits = new List<SearchHit>(Math.Clamp(limit, 1, 200));
        foreach (var candidate in repositories.Take(MaxRepositories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                SearchRepository(candidate, text, scope, Math.Clamp(limit, 1, 200), hits, cancellationToken);
            }
            catch (Exception exception) when (exception is GitRepositoryNotFoundException
                or LibGit2SharpException
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
            {
                // A missing or concurrently changed repository degrades only its own search results.
            }
            if (hits.Count >= limit) break;
        }
        return hits.Take(limit).ToArray();
    }

    private void SearchRepository(
        SearchRepositoryCandidate candidate,
        string query,
        SearchScope scope,
        int limit,
        List<SearchHit> hits,
        CancellationToken cancellationToken)
    {
        var context = new GitRepositoryContext(
            candidate.StorageName,
            pathResolver.ResolveRepositoryPath(candidate.StorageName));
        using var repository = new Repository(repositoryService.ResolveExistingPath(context));
        var fullName = $"{candidate.NamespaceSlug}/{candidate.RepositorySlug}";
        if (scope is SearchScope.All or SearchScope.Commit)
        {
            foreach (var commit in repository.Commits.Take(MaxCommitsPerRepository))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!commit.Message.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !commit.Author.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !commit.Id.Sha.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                hits.Add(new SearchHit(
                    SearchScope.Commit,
                    candidate.RepositoryId,
                    fullName,
                    commit.MessageShort,
                    commit.Id.Sha,
                    $"/{candidate.NamespaceSlug}/{candidate.RepositorySlug}/commit/{commit.Id.Sha}",
                    commit.Author.When));
                if (hits.Count >= limit) return;
            }
        }
        if (scope is not (SearchScope.All or SearchScope.Code) || repository.Head.Tip is not Commit head) return;
        var files = 0;
        foreach (var (path, blob) in EnumerateBlobs(head.Tree, string.Empty, cancellationToken))
        {
            if (++files > MaxFilesPerRepository || hits.Count >= limit) return;
            if (blob.Size > MaxCodeFileBytes || blob.IsBinary) continue;
            string content;
            using (var stream = blob.GetContentStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                content = reader.ReadToEnd();
            }
            var lineNumber = 0;
            foreach (var line in content.Split('\n'))
            {
                lineNumber++;
                if (!line.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                var encodedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
                hits.Add(new SearchHit(
                    SearchScope.Code,
                    candidate.RepositoryId,
                    fullName,
                    $"{path}:{lineNumber}",
                    Truncate(line.Trim(), 240),
                    $"/{candidate.NamespaceSlug}/{candidate.RepositorySlug}/blob/{encodedPath}?revision={head.Id.Sha}#L{lineNumber}"));
                break;
            }
        }
    }

    private static IEnumerable<(string Path, Blob Blob)> EnumerateBlobs(
        Tree tree,
        string parent,
        CancellationToken cancellationToken)
    {
        foreach (var entry in tree)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = string.IsNullOrEmpty(parent) ? entry.Name : $"{parent}/{entry.Name}";
            if (entry.Target is Tree child)
            {
                foreach (var result in EnumerateBlobs(child, path, cancellationToken)) yield return result;
            }
            else if (entry.Target is Blob blob)
            {
                yield return (path, blob);
            }
        }
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
