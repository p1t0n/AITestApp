using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExpertToJob.Agents.Agents;

/// <summary>Coverage summary for one candidate: requirements matched out of the total.</summary>
public sealed record ShortlistCoverage(int Matched, int Total);

/// <summary>One requirement's verdict for a candidate, with the evidence snippet when matched.</summary>
public sealed record ShortlistRequirementItem(
    string Text,
    bool Matched,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Snippet);

/// <summary>One shortlisted candidate in the pinned response contract.</summary>
public sealed record ShortlistCandidateItem(
    Guid ExpertId,
    string Name,
    string Title,
    double Score,
    ShortlistCoverage Coverage,
    IReadOnlyList<ShortlistRequirementItem> Requirements,
    string Rationale);

/// <summary>The pinned POST /agents/shortlist response: the requirement strings the retrieval ran
/// with, the tool's coverage-ranked candidates with per-candidate rationales, and (additive,
/// P1T-117) the full structured extraction — priorities, evidence spans, inferred badges,
/// ambiguities — for consumers that render more than the plain chips.</summary>
public sealed record ShortlistResponse(
    IReadOnlyList<string> Requirements,
    IReadOnlyList<ShortlistCandidateItem> Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JdRequirements? Extraction = null);

/// <summary>
/// Endpoint-side composition of the shortlist response. Every deterministic field — candidate
/// ids, names, scores, coverage, per-requirement evidence — comes from the captured tool result;
/// the model's turn-2 JSON contributes only per-candidate rationales. Corrupt model output can
/// therefore never corrupt the candidate list: unknown ids are ignored, and a missing, blank, or
/// unparseable rationale degrades to one templated from the tool's evidence.
/// </summary>
public static class ShortlistComposer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Composes the response. The caller guarantees <c>outcome.Tool</c> is non-null and
    /// error-free (upstream faults are handled before composition).</summary>
    public static ShortlistResponse Compose(ShortlistAgentOutcome outcome, JdRequirements? extraction = null)
    {
        var tool = outcome.Tool
            ?? throw new InvalidOperationException("Cannot compose a shortlist response without a captured tool result.");

        var rationales = ParseRationales(outcome.Reply.Text);

        var candidates = tool.Results
            .Select(candidate => new ShortlistCandidateItem(
                candidate.ExpertId,
                candidate.Name,
                candidate.Title,
                candidate.Score,
                new ShortlistCoverage(candidate.MatchedCount, candidate.TotalRequirements),
                candidate.Evidence
                    .Select(e => new ShortlistRequirementItem(e.Requirement, e.Matched, e.Snippet))
                    .ToList(),
                rationales.TryGetValue(candidate.ExpertId, out var rationale)
                    ? rationale
                    : TemplatedRationale(candidate)))
            .ToList();

        return new ShortlistResponse(outcome.Requirements, candidates, extraction);
    }

    /// <summary>Parses the model's minimal rationale JSON leniently: tolerates surrounding prose
    /// or markdown fences, skips entries with unknown/invalid ids or blank rationales, and returns
    /// an empty map when nothing parseable remains (the template then covers every candidate).</summary>
    private static Dictionary<Guid, string> ParseRationales(string modelText)
    {
        var rationales = new Dictionary<Guid, string>();
        var entries = TryParseEntries(modelText);
        if (entries is null)
        {
            return rationales;
        }

        foreach (var entry in entries)
        {
            if (entry?.ExpertId is { } idText
                && Guid.TryParse(idText, out var id)
                && !string.IsNullOrWhiteSpace(entry.Rationale))
            {
                rationales[id] = entry.Rationale.Trim();
            }
        }

        return rationales;
    }

    private static List<RationaleEntry?>? TryParseEntries(string modelText)
    {
        // Primary shape since P1T-117: the schema-constrained {"rationales":[...]} object.
        if (TryDeserializeObject(modelText) is { Rationales: { } wrapped })
        {
            return wrapped.Select(e => e is null ? null : new RationaleEntry(e.ExpertId, e.Rationale)).ToList();
        }

        if (TryDeserialize(modelText) is { } direct)
        {
            return direct;
        }

        // Second chance: the model wrapped the array in prose or a markdown fence — take the
        // outermost [...] span and retry.
        var start = modelText.IndexOf('[');
        var end = modelText.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return TryDeserialize(modelText[start..(end + 1)]);
        }

        return null;
    }

    private static List<RationaleEntry?>? TryDeserialize(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<List<RationaleEntry?>>(text, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ShortlistRationalePayload? TryDeserializeObject(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<ShortlistRationalePayload>(text.Trim(), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string TemplatedRationale(ShortlistToolCandidate candidate)
    {
        var matched = candidate.Evidence.Where(e => e.Matched).Select(e => e.Requirement).ToList();
        var missing = candidate.Evidence.Where(e => !e.Matched).Select(e => e.Requirement).ToList();

        var rationale = $"Matched {candidate.MatchedCount}/{candidate.TotalRequirements} requirements";
        if (matched.Count > 0)
        {
            rationale += $": {string.Join(", ", matched)}";
        }

        if (missing.Count > 0)
        {
            rationale += $"; missing: {string.Join(", ", missing)}";
        }

        return rationale + ".";
    }

    private sealed record RationaleEntry(string? ExpertId, string? Rationale);
}
