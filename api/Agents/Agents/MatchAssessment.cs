using System.Text.Json.Serialization;

namespace ExpertToJob.Agents.Agents;

/// <summary>
/// The Match agent's structured verdict (P1T-118): the deterministic facts (score, band) as typed
/// fields instead of regex-mined markdown, plus the full gap-analysis markdown that ships in
/// reports unchanged. Score and band are nullable on purpose — an expert that cannot be assessed
/// (not found, no evidence) is represented honestly, never invented.
/// </summary>
public sealed record MatchAssessment(
    [property: JsonPropertyName("score")] int? Score,
    [property: JsonPropertyName("band")] MatchBand? Band,
    [property: JsonPropertyName("gapAnalysisMarkdown")] string GapAnalysisMarkdown);

[JsonConverter(typeof(JsonStringEnumConverter<MatchBand>))]
public enum MatchBand
{
    Strong,
    Moderate,
    Weak,
    InsufficientEvidence,
}

public static class MatchBandExtensions
{
    /// <summary>The report/UI band string — identical to what <c>MatchAnswerParser</c> mined from
    /// markdown, so downstream contracts (band·score chips, proposal snapshots) stay unchanged.</summary>
    public static string ToDisplay(this MatchBand band) => band switch
    {
        MatchBand.InsufficientEvidence => "Insufficient evidence",
        _ => band.ToString(),
    };
}
