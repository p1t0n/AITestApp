using Microsoft.AspNetCore.Authorization;

namespace CvManager.Mcp;

/// <summary>
/// OAuth scope names for the MCP tools and the predicate that checks them against a token.
/// Read-only tools require <see cref="Read"/>, create/update require <see cref="Write"/>,
/// destructive (delete) tools require <see cref="Admin"/>.
/// </summary>
public static class McpScopes
{
    public const string Read = "mcp:read";
    public const string Write = "mcp:write";
    public const string Admin = "mcp:admin";

    /// <summary>
    /// Builds an authorization predicate that succeeds when the principal carries the given scope.
    /// Handles the OAuth "scope" claim (space-delimited) whether present once or split across claims.
    /// </summary>
    public static Func<AuthorizationHandlerContext, bool> Has(string scope) =>
        context => context.User
            .FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope);
}
