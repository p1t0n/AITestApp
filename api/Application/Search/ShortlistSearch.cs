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
/// One shortlisted expert: coverage (how many requirements they matched), a composite score, and
/// per-requirement evidence.
/// </summary>
public sealed record ShortlistCandidate(
    Guid ExpertId,
    string Name,
    string Title,
    double Score,
    int MatchedCount,
    int TotalRequirements,
    IReadOnlyList<ShortlistRequirementEvidence> Evidence);

/// <summary>The hits half of a shortlist search: the coverage-ranked candidates. Empty is a
/// perfectly good answer — nobody matched — and is why "no candidates" must not be reachable
/// through the fault case.</summary>
public sealed record ShortlistMatches(IReadOnlyList<ShortlistCandidate> Results)
{
    /// <summary>A successful search that found nobody.</summary>
    public static ShortlistMatches None { get; } = new([]);
}

/// <summary>The fault half: retrieval could not run at all (e.g. the embedding backend failed).
/// Callers degrade gracefully on this rather than surfacing an error.</summary>
public sealed record ShortlistSearchFault(string Error);

/// <summary>
/// What a shortlist search produced: candidates, or the fault that stopped retrieval — never both,
/// and never neither. A C# 15 union, so a caller cannot read the wrong half: every consumer
/// switches over the two cases and the compiler checks the switch is exhaustive, where the previous
/// <c>(Results, Error)</c> pair left that to a convention in a doc comment.
/// </summary>
public union ShortlistSearchOutcome(ShortlistMatches, ShortlistSearchFault);

/// <summary>
/// The published <c>roster_shortlist_search</c> tool result. This is the wire shape and nothing
/// else: the union above is the in-process seam, and this flattens it at the MCP edge so the JSON
/// agents already parse (<c>{ results, error }</c>, with <c>error</c> present and null on success)
/// stays byte for byte what it was. <c>RosterShortlistToolsTests</c> asserts it literally.
/// </summary>
public sealed record ShortlistSearchResult(IReadOnlyList<ShortlistCandidate> Results, string? Error = null)
{
    public static ShortlistSearchResult From(ShortlistSearchOutcome outcome) => outcome switch
    {
        ShortlistMatches matches => new(matches.Results),
        ShortlistSearchFault fault => new([], fault.Error),
    };
}

/// <summary>
/// Multi-requirement retrieval for JD-driven shortlisting: embed every requirement, match each
/// against the (optionally pre-filtered) roster, and merge coverage-first — candidates matching
/// more requirements rank above narrower, higher-similarity ones.
/// </summary>
public interface IShortlistSearchService
{
    Task<ShortlistSearchOutcome> SearchAsync(
        IReadOnlyList<string> requirements,
        SemanticSearchFilters? filters = null,
        int? topK = null,
        CancellationToken ct = default);
}
