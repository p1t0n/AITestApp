using System.Collections.Generic;
using System.Linq;
using ExpertToJob.Application.Common;
using ExpertToJob.Application.Search;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.Mcp.Auth;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// How the two <c>tools/call</c> filters compose (EXP-86). The server now runs both: the grant
/// filter refuses a tool the token does not carry, and the error filter turns a domain exception
/// into a structured tool error. They are registered in that order, and the thing that would
/// quietly undo the grant check is an error filter that swallowed or rewrote its refusal.
///
/// <para>Order is asserted through the real server rather than by inspecting the pipeline, because
/// the pipeline is the SDK's to compose — what has to hold is the behaviour, and it is observable:
/// a denial must reach the agent verbatim, and the denied tool's body must not have run.</para>
/// </summary>
public class McpCallToolFilterOrderTests
{
    private static string OnlyText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    [Fact]
    public async Task A_denied_call_is_refused_before_the_tool_body_and_the_error_filter_leaves_it_alone()
    {
        using var factory = McpTestHost.CreateFactory(
            nameof(A_denied_call_is_refused_before_the_tool_body_and_the_error_filter_leaves_it_alone));
        var expert = McpTestHost.SeedExpert(factory);

        // Every capability scope, but a grant set that does not include expert_delete.
        await using var client = await McpTestHost.ConnectAsync(
            factory,
            McpTestHost.MintToken(
                McpTestHost.ReadScope, McpTestHost.WriteScope, McpTestHost.AdminScope,
                McpScopes.ForTool("cv_get")));

        var result = await client.CallToolAsync(
            "expert_delete", new Dictionary<string, object?> { ["id"] = expert.Id.ToString() });

        // The refusal, unrewritten — not the error filter's shape, and not a success.
        result.IsError.Should().BeTrue();
        OnlyText(result).Should().Contain($"\"code\":\"{ToolGrantFilters.ForbiddenCode}\"");

        // And the body never ran: the expert the call named is still there. A seeded id is what
        // makes this assertion possible — an unknown id would have been refused either way.
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Experts.Any(e => e.Id == expert.Id).Should().BeTrue();
    }

    [Fact]
    public async Task A_granted_call_still_gets_the_error_filter()
    {
        // The other half of the order: the grant filter passing a call through must not take the
        // error mapping with it. A granted tool's domain exception is still a structured error.
        using var factory = McpTestHost
            .CreateFactory(nameof(A_granted_call_still_gets_the_error_filter))
            .WithWebHostBuilder(b => b.ConfigureServices(services =>
            {
                services.RemoveAll<ISemanticSearchService>();
                services.AddScoped<ISemanticSearchService>(_ => new NotFoundSearch());
            }));

        await using var client = await McpTestHost.ConnectAsync(
            factory,
            McpTestHost.MintToken(McpTestHost.ReadScope, McpScopes.ForTool("roster_semantic_search")));

        var result = await client.CallToolAsync(
            "roster_semantic_search", new Dictionary<string, object?> { ["query"] = "payments" });

        result.IsError.Should().BeTrue();
        OnlyText(result).Should().Be(
            """{"code":"not_found","message":"Expert \u0027z9\u0027 was not found.","fields":[]}""");
    }

    private sealed class NotFoundSearch : ISemanticSearchService
    {
        public Task<SemanticSearchResult> SearchAsync(
            string query, SemanticSearchFilters? filters, int? topK, CancellationToken ct) =>
            throw new NotFoundException("Expert", "z9");
    }
}
