namespace ExpertToJob.Application.Search;

/// <summary>
/// How one shortlist candidate fared against one requirement: matched or not, and when matched,
/// the best evidence snippet with its similarity.
/// </summary>
public sealed record ShortlistRequirementEvidence(
    string Requirement,
    bool Matched,
    string? Snippet = null,
    double? Similarity = null);

/// <summary>
/// One shortlisted employee: coverage (how many requirements they matched), a composite score, and
/// per-requirement evidence.
/// </summary>
public sealed record ShortlistCandidate(
    Guid EmployeeId,
    string Name,
    string Title,
    double Score,
    int MatchedCount,
    int TotalRequirements,
    IReadOnlyList<ShortlistRequirementEvidence> Evidence);

/// <summary>
/// Result of a shortlist search. <see cref="Error"/> is non-null only when retrieval could not run
/// (e.g. the embedding backend failed); callers degrade gracefully rather than surfacing an error.
/// </summary>
public sealed record ShortlistSearchResult(IReadOnlyList<ShortlistCandidate> Results, string? Error = null)
{
    public static ShortlistSearchResult Empty { get; } = new([]);
    public static ShortlistSearchResult Failed(string error) => new([], error);
}

/// <summary>
/// Multi-requirement retrieval for JD-driven shortlisting: embed every requirement, match each
/// against the (optionally pre-filtered) roster, and merge coverage-first — candidates matching
/// more requirements rank above narrower, higher-similarity ones.
/// </summary>
public interface IShortlistSearchService
{
    Task<ShortlistSearchResult> SearchAsync(
        IReadOnlyList<string> requirements,
        SemanticSearchFilters? filters = null,
        int? topK = null,
        CancellationToken ct = default);
}
