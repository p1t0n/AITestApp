namespace ExpertToJob.Agents.Configuration;

/// <summary>
/// <b>The one place</b> that says which agents sit behind each dock surface (EXP-31).
///
/// <para>A "surface" here is a pane of the SPA's agent dock — the <c>Surface</c> union in
/// <c>web/src/components/AgentWidget.tsx</c> — and the ids are that union's, spelled the same so
/// the SPA can index this map by the surface it is already showing. An agent key is what an agent
/// passes to <see cref="ChatProviderServiceCollectionExtensions.ResolveAgentChatClient"/>, which is
/// the only spelling a per-agent model override is keyed by.</para>
///
/// <para>The map is not derivable from the container: a keyed <see cref="Microsoft.Extensions.AI.IChatClient"/>
/// exists only for an agent someone overrode, and the endpoint a surface posts to is a lambda, not
/// a type. So it is written down — and held still by
/// <c>tests/Agents.Tests/AgentModelsEndpointTests</c>, which reads <c>api/Agents</c>'s own source
/// for every <c>ResolveAgentChatClient</c> call site and fails when a key it finds appears under no
/// surface. That is the drift this file would otherwise accumulate quietly: a new agent lands, the
/// dock keeps reporting the old model, and nothing is red.</para>
///
/// <para>A surface lists <em>every</em> agent a request from it can spend tokens on, not only the
/// headline one — Shortlist runs the JD extractor before its own model call, and Staffing runs
/// shortlist and match underneath. Reporting one model for a surface that uses three would be a
/// more precise-looking lie than reporting all three.</para>
/// </summary>
public static class AgentSurfaces
{
    /// <summary>Surface id → the agent keys a request from that surface resolves a chat client by,
    /// in the order the surface runs them. Duplicates across surfaces are expected and fine.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> AgentKeys =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["roster"] = ["roster-qa"],
            ["cv-tailoring"] = ["cv-tailoring"],
            // POST /agents/match extracts the JD's requirements before the match call.
            ["match"] = ["jd-extraction", "match"],
            ["interview-kit"] = ["jd-extraction", "interview-kit"],
            ["shortlist"] = ["jd-extraction", "shortlist"],
            // The staffing pipeline is shortlist + a match fan-out + its own narrative call.
            ["staffing"] = ["jd-extraction", "shortlist", "match", "staffing"],
            ["roster-scan"] = ["jd-extraction", "roster-scan"],
            ["bench"] = ["bench-report"],
            ["ingestion"] = ["resume-ingestion"],
        };

    /// <summary>
    /// Resolves each surface's <b>distinct, sorted</b> model set against the catalog: the per-agent
    /// override where one exists, the default otherwise. Distinct because the common case is that
    /// every agent behind a surface lands on the same model, and "Gemini · Gemini · Gemini" says
    /// nothing; sorted because the order agents run in is an implementation detail the dock's
    /// caption should not inherit.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ModelsBySurface(ChatModelCatalog catalog) =>
        AgentKeys.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value
                .Select(catalog.For)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(model => model, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
}
