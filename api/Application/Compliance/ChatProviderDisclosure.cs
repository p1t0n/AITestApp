namespace ExpertToJob.Application.Compliance;

/// <summary>
/// The chat backend a deployment sends career data to, as the Art. 15 disclosure needs to name it
/// (EXP-21, <c>manuals/adr-chat-provider-seam.md</c> §10 decision 12).
///
/// <para><b>A second enum rather than a reference to the seam's own.</b> The Application layer is
/// the single behaviour seam both the Web host and the MCP server sit on, and it may not reference
/// <c>api/Agents</c>. The member names are deliberately the same spelling <c>Ai:Chat:Provider</c>
/// takes, because the string a host reads out of configuration is what gets handed over here.</para>
/// </summary>
public enum DisclosedChatProvider
{
    /// <summary>Gemini serves chat, and — as always — embeddings too.</summary>
    Gemini,

    /// <summary>An Azure OpenAI deployment serves chat; embeddings stay on Google.</summary>
    AzureFoundry,
}

/// <summary>
/// Which chat provider the access view names, resolved once at startup from the same configuration
/// key the seam binds. A host supplies it; the Application layer never reads configuration itself.
///
/// <para><b>An unrecognised value is not an exception here.</b> The seam already refuses to start a
/// host on a provider name it does not know, so a host that is answering requests at all has a
/// valid one — which leaves only the case where nobody set the key in this host. Throwing on that
/// would take out a data subject's access request, the one thing Art. 15 exists to guarantee, so
/// the disclosure falls back to naming every recipient it might be. Over-telling somebody is
/// survivable; telling them nothing, or the wrong recipient, is the harm.</para>
/// </summary>
public sealed class ChatProviderDisclosure(DisclosedChatProvider? provider)
{
    /// <summary>The seam's own key, spelled once so a host cannot drift from it.</summary>
    public const string ConfigurationKey = "Ai:Chat:Provider";

    /// <summary>The configured provider, or null when configuration names none we recognise.</summary>
    public DisclosedChatProvider? Provider { get; } = provider;

    /// <summary>Parses a raw configuration value, answering null for anything unrecognised.</summary>
    public static ChatProviderDisclosure From(string? configuredValue) => new(Parse(configuredValue));

    /// <summary>
    /// Matched against the member names rather than parsed with <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>,
    /// and that is the point: <c>TryParse</c> accepts <c>"1"</c> and ORs a comma-separated list on
    /// any enum, so <c>"Gemini,AzureFoundry"</c> would arrive as a confident, defined
    /// <c>AzureFoundry</c>. On a disclosure path an ambiguous value must read as "we do not know",
    /// not as a name to print at somebody.
    /// </summary>
    private static DisclosedChatProvider? Parse(string? value)
    {
        var trimmed = value?.Trim();
        foreach (var candidate in Enum.GetValues<DisclosedChatProvider>())
        {
            if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
