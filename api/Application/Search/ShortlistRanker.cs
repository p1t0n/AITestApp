namespace CvManager.Application.Search;

/// <summary>One requirement's best chunk hit for an employee: similarity plus the evidence snippet.</summary>
public readonly record struct ShortlistMatch(double Similarity, string Snippet);

/// <summary>
/// A ranked candidate before hydration — the ranker works on employee ids only; the caller joins
/// names and titles afterwards.
/// </summary>
public sealed record ShortlistRankedCandidate(
    Guid EmployeeId,
    double Score,
    int MatchedCount,
    IReadOnlyList<ShortlistRequirementEvidence> Evidence);

/// <summary>
/// Pure coverage-first merge for shortlist search.
/// </summary>
public static class ShortlistRanker
{
    /// <summary>
    /// Ranks candidates by (requirements matched DESC, mean best-per-requirement similarity DESC)
    /// and keeps the top <paramref name="topK"/>. The composite score encodes that ordering:
    /// Score = (MatchedCount + mean similarity over matched requirements) / (total requirements + 1),
    /// so a candidate covering more requirements always scores above a narrower, closer match.
    /// </summary>
    public static IReadOnlyList<ShortlistRankedCandidate> Rank(
        IReadOnlyList<string> requirements,
        IReadOnlyList<IReadOnlyDictionary<Guid, ShortlistMatch>> matchesPerRequirement,
        int topK)
    {
        var employeeIds = matchesPerRequirement.SelectMany(m => m.Keys).Distinct();

        return employeeIds
            .Select(id =>
            {
                var evidence = requirements
                    .Select((req, i) => matchesPerRequirement[i].TryGetValue(id, out var match)
                        ? new ShortlistRequirementEvidence(req, true, match.Snippet, Math.Round(match.Similarity, 4))
                        : new ShortlistRequirementEvidence(req, false))
                    .ToList();

                var matched = evidence.Where(e => e.Matched).ToList();
                var meanSimilarity = matched.Count == 0 ? 0.0 : matched.Average(e => e.Similarity!.Value);
                var score = Math.Round((matched.Count + meanSimilarity) / (requirements.Count + 1), 4);

                return new { Candidate = new ShortlistRankedCandidate(id, score, matched.Count, evidence), meanSimilarity };
            })
            .OrderByDescending(x => x.Candidate.MatchedCount)
            .ThenByDescending(x => x.meanSimilarity)
            .ThenBy(x => x.Candidate.EmployeeId) // deterministic tie-break
            .Take(topK)
            .Select(x => x.Candidate)
            .ToList();
    }
}
