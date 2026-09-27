namespace ExpertToJob.Application.Compliance;

/// <summary>
/// The embeddings backend a deployment sends career narratives to, as the Art. 15 disclosure needs
/// to name it (EXP-66, <c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 16).
///
/// <para><b>A second enum rather than a reference to the seam's own</b>, for the same reason
/// <see cref="DisclosedChatProvider"/> is: <c>EmbeddingsProvider</c> lives in Infrastructure, which
/// the Application layer does not reference. The member names are deliberately the same spelling
/// <c>Ai:Embeddings:Provider</c> takes, because the string a host reads out of configuration is
/// what gets handed over here.</para>
/// </summary>
public enum DisclosedEmbeddingsProvider
{
    /// <summary>Gemini turns the career narrative into search embeddings.</summary>
    Gemini,

    /// <summary>An Azure OpenAI deployment does, EU-confined on <c>DataZoneStandard</c>.</summary>
    AzureFoundry,
}

/// <summary>
/// Which embeddings provider the access view names, resolved once at startup from the same
/// configuration key the seam binds. A host supplies it; the Application layer never reads
/// configuration itself.
///
/// <para><b>An unrecognised value is not an exception here</b>, exactly as in
/// <see cref="ChatProviderDisclosure"/>: the seam already refuses to start a host on a provider name
/// it does not know, so the only case left is a host that never had the key set. Throwing on that
/// would take out a data subject's access request, so the disclosure falls back to naming every
/// recipient it might be.</para>
/// </summary>
public sealed class EmbeddingsProviderDisclosure(DisclosedEmbeddingsProvider? provider)
{
    /// <summary>The seam's own key, spelled once so a host cannot drift from it.</summary>
    public const string ConfigurationKey = "Ai:Embeddings:Provider";

    /// <summary>The configured provider, or null when configuration names none we recognise.</summary>
    public DisclosedEmbeddingsProvider? Provider { get; } = provider;

    /// <summary>Parses a raw configuration value, answering null for anything unrecognised.</summary>
    public static EmbeddingsProviderDisclosure From(string? configuredValue) =>
        new(Parse(configuredValue));

    /// <summary>
    /// Matched against the member names rather than parsed with <c>Enum.TryParse</c>, which accepts
    /// <c>"1"</c> and ORs a comma-separated list on any enum — so <c>"Gemini,AzureFoundry"</c> would
    /// arrive as a confident, defined <c>AzureFoundry</c>. On a disclosure path an ambiguous value
    /// must read as "we do not know", not as a name to print at somebody.
    /// </summary>
    private static DisclosedEmbeddingsProvider? Parse(string? value)
    {
        var trimmed = value?.Trim();
        foreach (var candidate in Enum.GetValues<DisclosedEmbeddingsProvider>())
        {
            if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
