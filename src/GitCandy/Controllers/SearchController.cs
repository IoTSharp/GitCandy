using GitCandy.Authentication;
using GitCandy.Models;
using GitCandy.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

[Route("search")]
public sealed class SearchController(
    ICollaborationSearchService collaborationSearch,
    IGitContentSearchService gitContentSearch,
    ICurrentUser currentUser) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index(
        string? q,
        SearchScope scope = SearchScope.All,
        CancellationToken cancellationToken = default)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length is < 2 or > 200 || !Enum.IsDefined(scope))
        {
            return View(new SearchViewModel(query, scope, []));
        }
        var database = await collaborationSearch.SearchAsync(
            currentUser.UserId,
            currentUser.IsAdministrator,
            query,
            scope,
            cancellationToken: cancellationToken);
        var gitHits = scope is SearchScope.All or SearchScope.Commit or SearchScope.Code
            ? gitContentSearch.Search(database.Repositories, query, scope, 100, cancellationToken)
            : [];
        var hits = database.Hits.Concat(gitHits)
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        return View(new SearchViewModel(query, scope, hits));
    }
}
