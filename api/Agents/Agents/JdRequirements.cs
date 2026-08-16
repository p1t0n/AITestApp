using System.Text.Json.Serialization;

namespace CvManager.Agents.Agents;

/// <summary>
/// Structured reading of one job description (P1T-116) — the single source of requirements for
/// Shortlist, Match, Interview Kit, and Roster Scan. The schema is honest by construction: every
/// enum carries <c>Unspecified</c>, numeric/location facts are nullable, and
/// <see cref="Ambiguities"/> gives the model an explicit outlet for "the JD is unclear about X" —
/// so "not stated" never has to surface as an invented value.
/// </summary>
public sealed record JdRequirements(
    [property: JsonPropertyName("requirements")] IReadOnlyList<JdRequirement> Requirements,
    [property: JsonPropertyName("seniority")] JdSeniority Seniority,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("ambiguities")] IReadOnlyList<string> Ambiguities)
{
    /// <summary>The extraction rendered as a prompt block for downstream agents (Match, Interview
    /// Kit): the structured facts the model should assess against, instead of re-reading the raw
    /// JD from scratch. Honest markers travel along (unspecified, inferred).</summary>
    public string ToPromptBlock()
    {
        var lines = new List<string> { "Extracted role requirements (from the job description):" };
        foreach (var r in Requirements)
        {
            var years = r.MinYears is { } y ? $", min {y} yrs" : "";
            var inferred = r.Inferred ? " (inferred — no verbatim JD evidence)" : "";
            lines.Add($"- [{r.Priority}/{r.Kind}] {r.Text}{years}{inferred}");
        }

        lines.Add($"Seniority: {Seniority}; Location: {Location ?? "unspecified"}");
        if (Ambiguities.Count > 0)
        {
            lines.Add("JD ambiguities: " + string.Join("; ", Ambiguities));
        }

        return string.Join('\n', lines);
    }
}

/// <summary>One extracted requirement. <see cref="EvidenceSpan"/> is the model's verbatim quote
/// from the JD backing the requirement; the extractor verifies it (checked, never trusted) and
/// flips <see cref="Inferred"/> to true when the quote is not found — the requirement is kept,
/// badged, never silently stripped.</summary>
public sealed record JdRequirement(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("kind")] RequirementKind Kind,
    [property: JsonPropertyName("priority")] RequirementPriority Priority,
    [property: JsonPropertyName("minYears")] int? MinYears,
    [property: JsonPropertyName("evidenceSpan")] string? EvidenceSpan,
    [property: JsonPropertyName("inferred")] bool Inferred);

[JsonConverter(typeof(JsonStringEnumConverter<RequirementKind>))]
public enum RequirementKind
{
    Skill,
    Experience,
    Qualification,
    Language,
    Availability,
    Location,
    Other,
}

[JsonConverter(typeof(JsonStringEnumConverter<RequirementPriority>))]
public enum RequirementPriority
{
    MustHave,
    NiceToHave,
    Unspecified,
}

[JsonConverter(typeof(JsonStringEnumConverter<JdSeniority>))]
public enum JdSeniority
{
    Junior,
    Mid,
    Senior,
    Lead,
    Principal,
    Unspecified,
}
