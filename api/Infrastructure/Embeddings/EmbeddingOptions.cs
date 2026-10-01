namespace ExpertToJob.Infrastructure.Embeddings;

/// <summary>
/// One embeddings provider's settings. Each provider's embedding keys live in that provider's own
/// configuration block and share its <see cref="Endpoint"/> and <see cref="ApiKey"/> with chat
/// (<c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 2) — <see cref="SectionFor"/> is the
/// one place that mapping is written.
///
/// <para>The code defaults are per provider, in <see cref="Defaults"/>, rather than property
/// initialisers: <see cref="MinSimilarity"/> in particular is calibrated per embedding model, and a
/// shared default would be one provider's number quietly applied to another's vectors — the exact
/// failure the global <c>SemanticSearch:MinSimilarity</c> was retired for.</para>
///
/// <para>The credential is never bound from here in practice: <see cref="ApiKey"/> is the fallback
/// path, and the provider's own environment variable (<see cref="ApiKeyVariableFor"/>) is read
/// first. Never commit a real token.</para>
/// </summary>
public sealed class EmbeddingOptions
{
    /// <summary>OpenAI-compatible inference endpoint.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Embedding model id — a <b>deployment name</b> on Azure, as <c>Model</c> is for chat.</summary>
    public string EmbeddingModel { get; set; } = "";

    /// <summary>Requested output dimensionality. Shared across providers because it is the database
    /// that fixes it: the <c>ExpertSearchChunk</c> column is <c>vector(1536)</c>, so the request has
    /// to pin 1536 whoever serves it. Changing it is a schema migration (ADR §9).
    ///
    /// <para>A constant rather than a setting (EXP-88), because it was never one: no configuration
    /// file set it, and one that did would have been bound in silence and then rejected by Postgres
    /// per row, at write time. The column is the authority, so this is written once and cannot be
    /// reached from a configuration block.</para></summary>
    public const int Dimensions = 1536;

    /// <summary>API key. Prefer the provider's environment variable over config in real use.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Seconds the embedder's quota breaker stays open after retries exhaust on 429s.
    /// While open every embed call fails fast with <c>EmbeddingQuotaExceededException</c> instead
    /// of spending more requests against a cap that cannot clear quickly (P1T-99).</summary>
    public int QuotaBreakerSeconds { get; set; } = 1800;

    /// <summary>Minimum cosine similarity (0–1) for a chunk to count as a match. Below this it is
    /// dropped, so an off-topic query returns nothing rather than the least-bad rows. <b>Calibrated
    /// per embedding model</b>, which is why it lives here and not in a shared search section: on
    /// Azure vectors, Gemini's floor hides 70% of the correct matches (ADR §3).</summary>
    public double MinSimilarity { get; set; }

    /// <summary>The configuration section a provider's settings are bound from. Shared with chat:
    /// the endpoint and the credential really are the same ones (ADR §2 decision 2).</summary>
    public static string SectionFor(EmbeddingsProvider provider) => provider switch
    {
        EmbeddingsProvider.Gemini => "Ai:Gemini",
        EmbeddingsProvider.AzureFoundry => "Ai:AzureFoundry",
        var unmapped => throw new InvalidOperationException(
            $"No configuration section is mapped for embeddings provider {unmapped}. A new "
            + $"{nameof(EmbeddingsProvider)} member needs an entry here."),
    };

    /// <summary>The environment variable a provider's credential is read from, <b>and only that
    /// provider's</b>. Today's code read <c>GEMINI_API_KEY</c> whatever the endpoint, so a request
    /// aimed at Azure would have carried the Google key (EXP-53); this mapping is what makes that
    /// impossible rather than merely unlikely.</summary>
    public static string ApiKeyVariableFor(EmbeddingsProvider provider) => provider switch
    {
        EmbeddingsProvider.Gemini => "GEMINI_API_KEY",
        EmbeddingsProvider.AzureFoundry => "AZURE_FOUNDRY_API_KEY",
        var unmapped => throw new InvalidOperationException(
            $"No API key variable is mapped for embeddings provider {unmapped}. A new "
            + $"{nameof(EmbeddingsProvider)} member needs an entry here."),
    };

    /// <summary>A provider's code defaults, before its configuration block is bound over them.
    /// Every member of <see cref="EmbeddingsProvider"/> has an entry since EXP-67; a member added
    /// without one fails here, rather than binding an empty block and embedding against nothing.</summary>
    public static EmbeddingOptions Defaults(EmbeddingsProvider provider) => provider switch
    {
        EmbeddingsProvider.Gemini => new EmbeddingOptions
        {
            Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai",
            EmbeddingModel = "gemini-embedding-001",
            QuotaBreakerSeconds = 1800,
            // Measured 2026-08-01 over the frozen 24-expert corpus: plateau 0.540–0.575, recall@5
            // 1.0 with no false positives. See manuals/retrieval-eval-baseline.md.
            MinSimilarity = 0.55,
        },
        EmbeddingsProvider.AzureFoundry => new EmbeddingOptions
        {
            // The v1 endpoint of the resource EXP-56 provisioned, shared with chat
            // (api/Agents/appsettings.json names the same one). EmbeddingModel is the DEPLOYMENT
            // name, which happens to equal the model behind it here.
            Endpoint = "https://experttojob-openai-swc.openai.azure.com/openai/v1/",
            EmbeddingModel = "text-embedding-3-small",
            // A minute, not Gemini's half hour: Azure's cap is tokens per minute
            // (x-ratelimit-limit-tokens), not a daily request allowance, so a breaker that stayed
            // open for 1800s would keep search on the lexical fallback long after the throttle
            // cleared (ADR §2 decision 7).
            QuotaBreakerSeconds = 60,
            // Measured 2026-09-27 over the same frozen 24-expert corpus: plateau 0.285-0.350,
            // recall@5 1.0 with no false positives. Gemini's 0.55 applied to these vectors scores
            // recall@5 0.3030 — the reason this number is per provider (ADR §3).
            MinSimilarity = 0.30,
        },
        var unbuilt => throw new InvalidOperationException(
            $"'{EmbeddingServiceCollectionExtensions.ProviderKey}' is '{unbuilt}', which has no "
            + $"code defaults. A new {nameof(EmbeddingsProvider)} member needs an entry here as "
            + "well as a construction branch. See manuals/adr-embeddings-provider-seam.md."),
    };
}
