using System.Security.Claims;

namespace ExpertToJob.Mcp.Auth;

/// <summary>
/// The <b>Tool Grants</b> one token carries (P1T-149): the per-tool half of MCP authorization,
/// read off the caller's <c>mcp:tool:&lt;name&gt;</c> scopes. This is P1T-146's client-side Tool
/// Allowlist moved onto the identity that asserts it — <i>which tools this agent may see</i> is a
/// fact about <c>agent-roster-qa</c>, not a filter the client is trusted to remember to apply.
///
/// <para>Two rules, and the second one is the load-bearing one:</para>
/// <list type="number">
///   <item><description><b>Grants only ever narrow.</b> They compose with the capability scopes
///   (<c>mcp:read</c> / <c>mcp:write</c> / <c>mcp:admin</c>) rather than replacing them, so
///   <c>mcp:tool:expert_delete</c> on a read-only token buys nothing. A grant says "of the
///   tools you may already use, these"; it is not a second way to be entitled to one.</description></item>
///   <item><description><b>No grants means no narrowing.</b> A token carrying none is shown
///   everything its capability scopes carry — the same rule as an absent Tool Allowlist, for the
///   same reason: a forgotten client-scope assignment must not quietly cripple an agent, and the
///   interactive human client (<c>expert-to-job-mcp</c>) legitimately wants the whole surface.
///   Narrowing is always something an identity opts into.</description></item>
/// </list>
///
/// <para>Enforced in <see cref="ToolGrantFilters"/> at <c>tools/list</c> and <c>tools/call</c>,
/// not per tool method. A per-tool attribute would have to be remembered on every new tool; a
/// filter over the request cannot be forgotten, and it is the same place the SDK's own scope
/// authorization sits.</para>
/// </summary>
public sealed class McpToolGrants
{
    /// <summary>A token with no per-tool grants: shows everything its capability scopes carry.</summary>
    public static readonly McpToolGrants Unnarrowed = new(new HashSet<string>(StringComparer.Ordinal));

    private readonly HashSet<string> _granted;

    private McpToolGrants(HashSet<string> granted) => _granted = granted;

    /// <summary>Reads the grants off a caller's scopes. An unauthenticated principal (or one with
    /// no grant scopes) is <see cref="Unnarrowed"/> — the capability scopes still gate it.</summary>
    public static McpToolGrants Of(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return Unnarrowed;
        }

        var granted = McpScopes.ScopesOf(user)
            .Where(s => s.StartsWith(McpScopes.ToolScopePrefix, StringComparison.Ordinal))
            .Select(s => s[McpScopes.ToolScopePrefix.Length..])
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return granted.Count == 0 ? Unnarrowed : new McpToolGrants(granted);
    }

    /// <summary>True when the token grants no tool in particular, and so is narrowed by nothing.</summary>
    public bool ShowsEverything => _granted.Count == 0;

    /// <summary>The granted tool names, or empty when the token narrows nothing.</summary>
    public IReadOnlyCollection<string> ToolNames => _granted;

    /// <summary>Whether the grants let this tool through. Says nothing about capability — the
    /// <c>[Authorize]</c> policy on the tool itself is what decides that, and both must pass.</summary>
    public bool Allows(string toolName) => ShowsEverything || _granted.Contains(toolName);
}
