using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ExpertToJob.Mcp;

/// <summary>
/// Translates the Application layer's domain exceptions into a structured MCP tool error (via
/// <see cref="McpToolErrorMapper"/>) so the calling agent can read a machine code + per-field
/// detail and self-correct. Non-domain exceptions are left to the SDK's own tool-invocation
/// catch, which reports a generic failure carrying no machine code.
///
/// <para>One <c>tools/call</c> filter, where this used to be a <c>McpToolExecutor.RunAsync</c>
/// wrapper around all 37 tool bodies (EXP-86). The pipeline is the SDK's seam for exactly this,
/// and the server already uses it for the grant checks in
/// <see cref="Auth.ToolGrantFilters"/> — a cross-cutting rule enforced in one place cannot be
/// the one a newly added tool forgets to opt into.</para>
/// </summary>
public static class McpToolErrorFilter
{
    /// <summary>
    /// Registers the error filter. It is independent of its position relative to the grant
    /// filters: a grant refusal is <em>returned</em> rather than thrown, so there is nothing here
    /// to catch and nothing to rewrite whichever of the two runs first.
    /// </summary>
    public static IMcpServerBuilder AddToolErrorFilter(this IMcpServerBuilder builder)
    {
        builder.Services.Configure<McpServerOptions>(options =>
        {
            options.Filters.Request.CallToolFilters.Add(next => async (request, ct) =>
            {
                try
                {
                    return await next(request, ct);
                }
                catch (Exception ex) when (McpToolErrorMapper.Map(ex) is { } error)
                {
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(error, JsonSerializerOptions.Web) }],
                    };
                }
            });
        });

        return builder;
    }
}
