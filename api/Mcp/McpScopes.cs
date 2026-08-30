using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace CvManager.Mcp;

/// <summary>
/// OAuth scope names for the MCP tools and the predicate that checks them against a token.
/// Read-only tools require <see cref="Read"/>, create/update require <see cref="Write"/>,
/// destructive (delete) tools require <see cref="Admin"/>.
///
/// <para>Those three are the CAPABILITY axis — what kind of thing a caller may do. A second,
/// finer axis rides on the same claim: a <see cref="ToolScopePrefix"/> scope names ONE tool, and
/// a token carrying any of them is narrowed to exactly those (P1T-149). See
/// <see cref="Auth.McpToolGrants"/> for the rule.</para>
/// </summary>
public static class McpScopes
{
    public const string Read = "mcp:read";
    public const string Write = "mcp:write";
    public const string Admin = "mcp:admin";

    /// <summary>
    /// Prefix of a per-tool grant scope: <c>mcp:tool:cv_get</c> names <c>cv_get</c> and nothing
    /// else. Deliberately a separate prefix from the capability scopes above so the two axes can
    /// never be confused for one another — a grant cannot stand in for <c>mcp:write</c>, and
    /// <c>mcp:read</c> cannot stand in for a grant.
    /// </summary>
    public const string ToolScopePrefix = "mcp:tool:";

    /// <summary>The scope that grants one named tool, e.g. <c>mcp:tool:cv_get</c>.</summary>
    public static string ForTool(string toolName) => ToolScopePrefix + toolName;

    /// <summary>
    /// Every scope the principal carries. Handles the OAuth "scope" claim (space-delimited)
    /// whether present once or split across claims.
    /// </summary>
    public static IEnumerable<string> ScopesOf(ClaimsPrincipal user) =>
        user.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Builds an authorization predicate that succeeds when the principal carries the given scope.
    /// </summary>
    public static Func<AuthorizationHandlerContext, bool> Has(string scope) =>
        context => ScopesOf(context.User).Contains(scope);
}
