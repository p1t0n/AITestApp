using ExpertToJob.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExpertToJob.Infrastructure.Search;

/// <summary>
/// How much of the search index the active embedder has actually embedded: chunks carrying its tag
/// with a vector, over all chunks (<c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 13).
///
/// <para>Shared by the reconciler (which publishes it as a gauge after each pass) and by the search
/// query (which turns anything below 1 into a note on the tool result). One definition, because two
/// would eventually disagree and the number's whole job is to be believed.</para>
/// </summary>
public static class IndexCoverage
{
    /// <summary>
    /// One grouped query, so a search call pays a single extra round trip rather than two counts.
    ///
    /// <para>An empty index reads <b>1</b>. Coverage answers "how much of what exists is on the
    /// current model", and on a brand-new deployment the honest answer is "all of it": 0 would put
    /// a permanent "index rebuilding: 0%" note on every search of an empty roster.</para>
    /// </summary>
    public static async Task<double> MeasureAsync(AppDbContext db, string tag, CancellationToken ct)
    {
        var counts = await db.ExpertSearchChunks
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Covered = g.Count(c => c.Embedding != null && c.Model == tag),
            })
            .FirstOrDefaultAsync(ct);

        return counts is null || counts.Total == 0
            ? 1.0
            : (double)counts.Covered / counts.Total;
    }

    /// <summary>
    /// The note a partially re-embedded index puts on a search result, so the agent can say the
    /// answer may be incomplete rather than presenting a narrowed roster as the whole one. Null
    /// when coverage is 1 — a note that is always there is a note nobody reads.
    /// </summary>
    public static string? NoteFor(double coverage)
        => coverage >= 1.0
            ? null
            : $"index rebuilding: {Math.Round(coverage * 100)}% re-embedded. Experts whose "
              + "career narratives are not yet re-embedded cannot match this search yet.";
}
