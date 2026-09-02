using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExpertToJob.Application.Compliance;

/// <summary>
/// Removes one person from a persisted staffing handoff document, leaving the document itself
/// intact (P1T-186). The proposal is a decision a human made and Art. 17(3)(e) covers the fact that
/// they made it, so the envelope survives — the approver must still be able to read what was
/// decided — while everything the report says <em>about the erased person</em> goes.
///
/// <para><b>Why this walks JSON rather than the typed record.</b> The document's types
/// (<c>StaffingHandoffDocument</c>, <c>StaffingReport</c>) live in the Agents host, and the Web host
/// that serves erasure cannot reference them. The alternative — a second copy of those records over
/// here — is the drift this whole slice exists to prevent. So the rewrite addresses named paths,
/// and <c>Agents.Tests/HandoffPackageScrubTests</c> closes the loop from the other side: it feeds a
/// real serialized document through this code and deserializes the result with the real
/// <c>StaffingHandoffDocument.TryDeserialize</c>, asserting the document still parses, that exactly
/// these fields are gone, and that every other field survives untouched. Blind jsonb surgery is
/// what this is not: unknown shapes are left alone and a document that does not parse is left
/// exactly as it was found.</para>
/// </summary>
public static class HandoffPackageScrub
{
    /// <summary>Same options the document is written with — camelCase, so the paths below match.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Nulls everything the report says about <paramref name="expertId"/>, and returns the rewritten
    /// JSON — or the original string when there is nothing to change, when the column is empty, or
    /// when it does not parse. Never throws: a proposal whose package is unreadable is already
    /// handled that way by its readers, and erasure must not be the thing that fails on it.
    /// </summary>
    public static string? Remove(string? packageJson, Guid expertId)
    {
        if (string.IsNullOrWhiteSpace(packageJson))
        {
            return packageJson;
        }

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(packageJson);
        }
        catch (JsonException)
        {
            // A column that does not parse holds nothing this code can identify as personal, and
            // rewriting it blind would destroy a decision record to no purpose.
            return packageJson;
        }

        var report = document?["report"];
        if (report is null)
        {
            return packageJson;
        }

        var changed = false;
        var id = expertId.ToString();

        if (report["candidates"] is JsonArray candidates)
        {
            foreach (var candidate in candidates.OfType<JsonObject>())
            {
                if (!IdMatches(candidate["expertId"], id))
                {
                    continue;
                }

                changed |= Blank(candidate, "name");
                changed |= Blank(candidate, "title");
                changed |= Blank(candidate, "rationale");
                changed |= Blank(candidate["match"] as JsonObject, "answer");

                // The evidence snippets are lifted verbatim out of the person's CV — the same words
                // the chunk store holds, quoted into somebody else's decision record.
                if (candidate["shortlist"]?["requirements"] is JsonArray requirements)
                {
                    foreach (var requirement in requirements.OfType<JsonObject>())
                    {
                        changed |= Blank(requirement, "snippet");
                    }
                }
            }
        }

        // The recommendation names one candidate and argues for them in prose.
        if (report["recommendation"] is JsonObject recommendation
            && IdMatches(recommendation["expertId"], id))
        {
            changed |= Blank(recommendation, "narrative");
        }

        return changed ? document!.ToJsonString(Json) : packageJson;
    }

    /// <summary>The six paths this scrub is responsible for, as the completeness test reads them.
    /// Named here rather than in the test so the list cannot drift from the code that clears it.</summary>
    public static IReadOnlyList<string> ScrubbedPaths { get; } =
    [
        "report.candidates[].name",
        "report.candidates[].title",
        "report.candidates[].rationale",
        "report.candidates[].match.answer",
        "report.candidates[].shortlist.requirements[].snippet",
        "report.recommendation.narrative",
    ];

    /// <summary>Compares an id without assuming the stored node is a string — a document written by
    /// some other version is a thing to skip, not a thing to throw on.</summary>
    private static bool IdMatches(JsonNode? node, string expertId) =>
        node is JsonValue value
        && value.TryGetValue<string>(out var stored)
        && string.Equals(stored, expertId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Nulls one property if it is there and carries anything. Returns whether it changed
    /// something, so an untouched document is written back byte-identical.</summary>
    private static bool Blank(JsonObject? owner, string property)
    {
        if (owner is null || !owner.TryGetPropertyValue(property, out var value) || value is null)
        {
            return false;
        }

        owner[property] = null;
        return true;
    }
}
