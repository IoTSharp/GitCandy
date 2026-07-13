using GitCandy.Search;

namespace GitCandy.Models;

public sealed record SearchViewModel(
    string Query,
    SearchScope Scope,
    IReadOnlyList<SearchHit> Hits);
