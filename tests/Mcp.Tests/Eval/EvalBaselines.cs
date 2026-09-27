using ExpertToJob.Infrastructure.Embeddings;

namespace ExpertToJob.Mcp.Tests.Eval;

/// <summary>
/// Committed retrieval-quality baselines the live regression test asserts against. This is THE
/// place baseline numbers live.
///
/// <para><b>One floor per provider since EXP-67</b> (<c>manuals/adr-embeddings-provider-seam.md</c>
/// §2 decision 8), and each is measured at <em>that provider's own</em>
/// <c>MinSimilarity</c> — the pair is the measurement, and a floor quoted without the threshold it
/// was taken at means nothing. Gemini's 0.55 applied to Azure's vectors scores recall@5 0.3030, so
/// a single shared number would read as a catastrophic regression the day the default moved, which
/// is precisely the false alarm a per-provider baseline exists to prevent.</para>
///
/// <para>Re-measure with <c>dotnet run --project tools/RetrievalEval -- --provider &lt;name&gt;</c>.</para>
/// </summary>
public static class EvalBaselines
{
    /// <summary>Slack subtracted from the baseline before asserting, absorbing run-to-run noise.</summary>
    public const double Tolerance = 0.05;

    /// <summary>
    /// The measured recall@5 floor for one provider, at its own similarity threshold.
    ///
    /// <list type="bullet">
    /// <item><description><b>Gemini</b>, <c>gemini-embedding-001</c> at 0.55, measured 2026-08-01
    /// over the frozen 24-expert corpus and 39-query golden set: recall@5 1.0000, MRR 1.0000,
    /// negative-FP rate 0.0000 (see <c>manuals/retrieval-eval-baseline.md</c> for the full sweep —
    /// the 0.30 floor tuned for the retired OpenAI model let every negative query through on
    /// Gemini).</description></item>
    /// <item><description><b>Azure</b>, <c>text-embedding-3-small</c> at 0.30, measured 2026-09-27
    /// over the same corpus (EXP-57): plateau 0.285–0.350, recall@5 1.0000, MRR 0.9848, no false
    /// positives. Row for row identical to the 2026-07-11 baseline, which measured the same model
    /// through GitHub Models — same model, same vectors.</description></item>
    /// </list>
    /// </summary>
    public static double RecallAt5For(EmbeddingsProvider provider) => provider switch
    {
        EmbeddingsProvider.Gemini => 1.0,
        EmbeddingsProvider.AzureFoundry => 1.0,
        var unmeasured => throw new InvalidOperationException(
            $"No retrieval baseline has been measured for {unmeasured}. A new "
            + $"{nameof(EmbeddingsProvider)} member needs a run of tools/RetrievalEval behind it "
            + "before its gate can mean anything."),
    };
}
