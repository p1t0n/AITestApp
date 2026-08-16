using System.Text.Json;
using CvManager.Agents.Handoff;

namespace CvManager.Agents.Staffing;

/// <summary>
/// The persisted shape of a staffing run's handoff (P1T-133): the accumulated
/// <see cref="HandoffPackage"/> unrolled around the <b>full</b> <see cref="StaffingReport"/> —
/// requirements, per-requirement evidence, full match markdown, notes, recommendation narrative,
/// and the extraction. This is what lands in the proposal's jsonb column, so the approver can
/// decide without re-running anything. No truncation on purpose: the report IS the findings.
/// </summary>
public sealed record StaffingHandoffDocument(
    IReadOnlyDictionary<string, string?> Inputs,
    StaffingReport Report,
    RunProvenance Provenance,
    IReadOnlyList<StageSlice> Slices,
    IReadOnlyList<DegradationEntry> Degradations)
{
    /// <summary>camelCase like the wire report, so the persisted document and the SSE payload
    /// spell fields identically.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static StaffingHandoffDocument From(HandoffPackage package, StaffingReport report) =>
        new(package.Inputs, report, package.Provenance, package.Slices, package.Degradations);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    /// <summary>Null for a null/legacy/corrupt column — readers degrade to the snapshot columns,
    /// they never throw over a stored document.</summary>
    public static StaffingHandoffDocument? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StaffingHandoffDocument>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
