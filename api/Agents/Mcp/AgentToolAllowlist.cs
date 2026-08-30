using Microsoft.Extensions.AI;

namespace CvManager.Agents.Mcp;

/// <summary>
/// One agent's <b>Tool Allowlist</b> (P1T-146): the subset of the tools its token carries that it
/// is shown. Applied once, where the MCP tool list arrives, so no agent can widen its own surface
/// by forgetting to filter.
///
/// <para>Why it is worth a type: an unused tool is not free. Its schema is part of Baseline Prompt
/// Size, and Turn Amplification re-sends that on every iteration — roster-qa paid for seven tools
/// it never called, ten times over, 26% of a 160,220-token run. The second effect is thrash: seven
/// tools the model has no business in are seven more things for it to pick wrongly.</para>
///
/// <para>An empty allowlist means "everything the token carries". Narrowing is an explicit act:
/// a missing config key must never quietly cripple an agent.</para>
/// </summary>
public sealed class AgentToolAllowlist
{
    /// <summary>Shows every tool the token carries. What an agent with no configured list gets.</summary>
    public static readonly AgentToolAllowlist All = new([]);

    private readonly HashSet<string> _allowed;

    public AgentToolAllowlist(IEnumerable<string> toolNames) =>
        _allowed = new HashSet<string>(
            toolNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
            StringComparer.Ordinal);

    /// <summary>The configured names, or empty when the allowlist shows everything.</summary>
    public IReadOnlyCollection<string> ToolNames => _allowed;

    /// <summary>True when no narrowing is configured.</summary>
    public bool ShowsEverything => _allowed.Count == 0;

    /// <summary>Narrows <paramref name="offered"/> to the allowlist, preserving the server's order.
    /// Returns <paramref name="offered"/> unchanged when nothing is configured.</summary>
    public IReadOnlyList<AITool> Apply(IReadOnlyList<AITool> offered) =>
        ShowsEverything ? offered : offered.Where(t => _allowed.Contains(t.Name)).ToList();

    /// <summary>Allowlisted names the server did not advertise. Never empty by accident: either a
    /// typo, or the agent's scope no longer carries a tool its list still asks for — both of which
    /// silently narrow the surface, so the caller reports them rather than shipping a crippled
    /// agent quietly.</summary>
    public IReadOnlyList<string> MissingFrom(IReadOnlyList<AITool> offered) =>
        ShowsEverything
            ? []
            : _allowed.Except(offered.Select(t => t.Name), StringComparer.Ordinal).Order().ToList();
}
