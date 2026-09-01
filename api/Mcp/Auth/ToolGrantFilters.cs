using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp.Auth;

/// <summary>
/// Enforces <see cref="McpToolGrants"/> on the two requests that can reach a tool: <c>tools/list</c>
/// advertises only what the caller's token grants, and <c>tools/call</c> refuses anything it does
/// not — the second half being the one P1T-146's client-side allowlist structurally could not do.
/// A client that filtered its own list could always still call what it had discarded.
/// </summary>
public static class ToolGrantFilters
{
    /// <summary>The machine code a grant refusal carries, alongside the Application layer's
    /// <c>not_found</c> / <c>conflict</c> / <c>validation</c>.</summary>
    public const string ForbiddenCode = "forbidden";

    private static readonly JsonSerializerOptions ErrorJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Registers the grant filters. Composes with (and is independent of the ordering of) the
    /// SDK's <c>AddAuthorizationFilters()</c>: the list filter narrows whatever survived the
    /// capability scopes, and a call has to clear both gates whichever runs first.
    /// </summary>
    public static IMcpServerBuilder AddToolGrantFilters(this IMcpServerBuilder builder)
    {
        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Filters.Request.ListToolsFilters.Add(next => async (request, ct) =>
            {
                var result = await next(request, ct);
                var grants = McpToolGrants.Of(request.User);
                if (grants.ShowsEverything)
                {
                    return result;
                }

                // A new result rather than a mutated one: the handler is free to hand back a
                // shared listing, and narrowing it in place would narrow it for every caller.
                // Narrowing per page is correct even if the surface ever grows enough to page —
                // the cursor is passed through untouched and clients aggregate pages — though it
                // can leave a page short, which is a listing detail and not a correctness one.
                return new ListToolsResult
                {
                    Tools = [.. result.Tools.Where(t => grants.Allows(t.Name))],
                    NextCursor = result.NextCursor,
                };
            });

            options.Filters.Request.CallToolFilters.Add(next => (request, ct) =>
            {
                var name = request.Params?.Name;
                return name is not null && !McpToolGrants.Of(request.User).Allows(name)
                    ? ValueTask.FromResult(Refuse(name))
                    : next(request, ct);
            });
        });

        return builder;
    }

    /// <summary>
    /// The refusal, in the same structured shape as every other tool failure
    /// (<see cref="McpToolErrorMapper"/>): a machine code and a message naming the tool, returned
    /// as an error RESULT rather than thrown as a protocol fault.
    ///
    /// <para>Deliberate, and it is where this differs from the SDK's own scope refusal. An agent
    /// reads an error result and picks something else; a protocol fault ends its run. "Degrade,
    /// never 500" applies to a tool the model should not have reached for as much as to one that
    /// broke — and returning it rather than throwing also makes the behaviour independent of
    /// where this filter sits in the pipeline.</para>
    /// </summary>
    private static CallToolResult Refuse(string toolName)
    {
        var error = new McpToolError(
            ForbiddenCode,
            $"'{toolName}' is not among the tools this token grants. It is not part of this "
            + "agent's tool surface — use one of the tools you were listed.",
            []);

        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(error, ErrorJson) }],
        };
    }
}
