using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;

namespace ExpertToJob.Application.Search;

/// <summary>
/// Renders an expert's free-text career narrative into the set of chunks that semantic roster
/// search embeds: one chunk per work <see cref="Experience"/> (role + company + dates header, then
/// its summary and achievements), one chunk for the expert's professional summary, plus one
/// fine-grained chunk per non-blank <see cref="Achievement"/> bullet (for exemplar-style retrieval;
/// the experience chunk keeps the bullets rolled in as the expert-level narrative unit).
///
/// <para>Pure and deterministic: the same expert always renders the same text and the same
/// SHA-256 <c>ContentHash</c>, which is what lets the reconciler detect real changes.</para>
/// </summary>
public static class ChunkProjection
{
    /// <summary>
    /// Project an expert into its desired chunks. Requires <see cref="Expert.Experiences"/>
    /// (with their achievements) to be loaded.
    /// </summary>
    public static IReadOnlyList<DesiredChunk> Project(Expert expert)
    {
        var chunks = new List<DesiredChunk>();

        if (!string.IsNullOrWhiteSpace(expert.Summary))
        {
            chunks.Add(Make(expert.Id, SearchChunkSource.Summary, expert.Id, expert.Summary!.Trim()));
        }

        foreach (var experience in expert.Experiences)
        {
            chunks.Add(Make(expert.Id, SearchChunkSource.Experience, experience.Id, RenderExperience(experience)));

            // One fine-grained chunk per achievement bullet (the experience chunk above keeps the
            // bullets rolled in as well — it stays the narrative unit for expert-level search).
            foreach (var achievement in experience.Achievements.OrderBy(a => a.Order))
            {
                if (!string.IsNullOrWhiteSpace(achievement.Text))
                {
                    chunks.Add(Make(expert.Id, SearchChunkSource.Achievement, achievement.Id, achievement.Text.Trim()));
                }
            }
        }

        return chunks;
    }

    /// <summary>SHA-256 (lowercase hex) of the content — the reconciler's change signal.</summary>
    public static string Hash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }

    private static DesiredChunk Make(Guid expertId, SearchChunkSource type, Guid sourceId, string content)
        => new(expertId, type, sourceId, content, Hash(content));

    private static string RenderExperience(Experience experience)
    {
        var sb = new StringBuilder();

        // Header: "Title @ Company (2019-03–present)" — anchors the narrative to role, org, dates.
        sb.Append(experience.Title).Append(" @ ").Append(experience.Company);
        sb.Append(" (").Append(FormatMonth(experience.StartDate)).Append('–');
        sb.Append(experience.EndDate is { } end ? FormatMonth(end) : "present").Append(')');

        if (!string.IsNullOrWhiteSpace(experience.Summary))
        {
            sb.Append('\n').Append(experience.Summary!.Trim());
        }

        // Achievements in display order, one bullet per line.
        foreach (var achievement in experience.Achievements.OrderBy(a => a.Order))
        {
            if (!string.IsNullOrWhiteSpace(achievement.Text))
            {
                sb.Append("\n- ").Append(achievement.Text.Trim());
            }
        }

        return sb.ToString();
    }

    private static string FormatMonth(DateOnly date)
        => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
