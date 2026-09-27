namespace ExpertToJob.Infrastructure.Embeddings;

/// <summary>
/// The backend that turns a career narrative into a vector, named by
/// <c>Ai:Embeddings:Provider</c> (<c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 1).
/// One provider is active per deployment: there is no failover and no per-call routing.
///
/// <para><b>Separate from <c>Ai:Chat:Provider</c>, on purpose.</b> The two backends are genuinely
/// independent — chat runs in the Agents host, embeddings in the MCP host — and one key would turn
/// "switch chat back to Gemini to compare" into "and re-embed the whole index" (ADR §4). This
/// supersedes the chat ADR's §2 decision 3, which assumed embeddings would keep calling Google
/// whatever chat does.</para>
///
/// <para><b>An enum rather than a string</b>, for the same reason chat's is: it makes an unknown
/// value fail at the configuration edge instead of somewhere further in. Nothing persists this
/// type; where a provider name is written down it is <c>provider.ToString()</c>.</para>
///
/// <para>This lives in Infrastructure rather than beside a host, because Web, MCP and
/// <c>tools/RetrievalEval</c> all read it.</para>
/// </summary>
public enum EmbeddingsProvider
{
    /// <summary>The incumbent: <c>gemini-embedding-001</c> through Gemini's OpenAI-compatible
    /// endpoint. Development and demo only — Google's free-tier terms make it unsuitable for real
    /// people (ADR §5).</summary>
    Gemini,

    /// <summary>An Azure OpenAI <c>text-embedding-3-small</c> deployment, EU-confined on
    /// <c>DataZoneStandard</c> (ADR §7). Named here before it is built: its construction branch
    /// arrives with EXP-67.</summary>
    AzureFoundry,
}
