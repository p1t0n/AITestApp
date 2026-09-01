using System.Text.Json;

namespace ExpertToJob.Agents.Agents;

/// <summary>One vetted rewrite in the pinned response contract. Ids and the original bullet text
/// come from CV data (the captured cv_get result); only <see cref="Rewritten"/> is model text.</summary>
public sealed record TailoringRewriteItem(
    Guid ExperienceId,
    Guid AchievementId,
    string Original,
    string Rewritten);

/// <summary>The pinned POST /agents/cv-tailoring response: the advisory markdown exactly as
/// before (existing consumers keep working), plus the vetted achievement-bullet rewrites.</summary>
public sealed record TailoringResponse(string Answer, IReadOnlyList<TailoringRewriteItem> Rewrites);

/// <summary>
/// Endpoint-side composition of the hybrid tailoring response. The answer is turn 1's markdown
/// verbatim; every deterministic rewrite field — experienceId, achievementId, the original bullet
/// — is resolved from the captured cv_get result (the Agents service may not query employee data
/// directly; MCP is the boundary), and the model's turn-2 JSON contributes only rewritten strings.
/// Entries with unknown/unselected/corrupted ids or blank text are dropped, and each survivor
/// passes the <see cref="FabricationGuard"/> against its original bullet, its experience context,
/// and the exemplars shown this run. Corruption always degrades to fewer rewrites — never to a
/// failed request, and never to fabricated ids or originals.
/// </summary>
public static class TailoringComposer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static TailoringResponse Compose(TailoringAgentOutcome outcome, ILogger logger)
    {
        return new TailoringResponse(outcome.Reply.Text, ComposeRewrites(outcome, logger));
    }

    private static List<TailoringRewriteItem> ComposeRewrites(TailoringAgentOutcome outcome, ILogger logger)
    {
        var rewrites = new List<TailoringRewriteItem>();
        if (outcome.Cv is not { } cv)
        {
            return rewrites;
        }

        var entries = TryParseEntries(outcome.RewritesText);
        if (entries is null)
        {
            return rewrites;
        }

        // Original bullets and their experience context, keyed by achievement id — CV data only.
        var bullets = new Dictionary<Guid, (Guid ExperienceId, string Original, string Context)>();
        foreach (var experience in cv.Experiences)
        {
            var context = string.Join(" ", new[]
            {
                experience.Company, experience.Title, experience.Period, experience.Summary,
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

            foreach (var achievement in experience.Achievements)
            {
                bullets[achievement.Id] = (experience.Id, achievement.Text, context);
            }
        }

        // The overlap rule vets against every exemplar shown this run, whichever bullet it
        // arrived for — a phrase borrowed across bullets is still borrowed.
        var exemplarTexts = outcome.Exemplars?.Results
            .SelectMany(r => r.Exemplars)
            .Select(e => e.Text)
            .ToList() ?? [];

        // When the exemplar call happened, the selection it carried is the contract: the model
        // may only rewrite bullets it selected. Without that call (degrade path) CV membership
        // alone decides.
        var selected = outcome.SelectedAchievementIds.Count > 0
            ? outcome.SelectedAchievementIds.ToHashSet()
            : null;

        var seen = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (entry?.AchievementId is not { } idText
                || !Guid.TryParse(idText, out var id)
                || !bullets.TryGetValue(id, out var bullet)
                || (selected is not null && !selected.Contains(id))
                || string.IsNullOrWhiteSpace(entry.Rewritten)
                || !seen.Add(id))
            {
                continue;
            }

            var rewritten = entry.Rewritten.Trim();
            if (FabricationGuard.Check(rewritten, bullet.Original, bullet.Context, exemplarTexts) is { } violation)
            {
                logger.LogWarning(
                    "Dropping tailoring rewrite for achievement {AchievementId}: {Violation}.",
                    id, violation);
                continue;
            }

            rewrites.Add(new TailoringRewriteItem(bullet.ExperienceId, id, bullet.Original, rewritten));
        }

        return rewrites;
    }

    /// <summary>Parses the model's minimal rewrites JSON leniently: tolerates surrounding prose
    /// or markdown fences, and returns null when nothing parseable remains (answer-only degrade).</summary>
    private static List<RewriteEntry?>? TryParseEntries(string modelText)
    {
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

    private static List<RewriteEntry?>? TryDeserialize(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<List<RewriteEntry?>>(text, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RewriteEntry(string? AchievementId, string? Rewritten);
}
