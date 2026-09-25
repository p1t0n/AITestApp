using FluentAssertions;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// Pins the Streamable HTTP transport to stateful sessions. The MCP SDK flipped the default to
/// stateless in 2.0 (modelcontextprotocol/csharp-sdk#1610), which silently drops the
/// <c>Mcp-Session-Id</c> handshake, the GET/DELETE SSE streams and every server-to-client request.
/// A package bump is not the place to make that call, so the server states its mode in
/// <c>Program.cs</c> and this test fails if a future bump — or an edit — changes it without saying so.
/// </summary>
public class McpTransportModeTests
{
    [Fact]
    public async Task Connected_client_is_issued_a_session_id()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Connected_client_is_issued_a_session_id));
        await using var client = await McpTestHost.ConnectAsync(factory);

        client.SessionId.Should().NotBeNullOrEmpty(
            "the MCP server runs stateful Streamable HTTP sessions; going stateless is its own decision, not a side effect of a package bump");
    }

    /// <summary>
    /// SDK 2.2 added list-result caching hints (SEP-2549). Every tools/list here is per-caller —
    /// capability scopes and Tool Grants both narrow it — so a <c>Public</c> scope would invite a
    /// shared cache to serve one token's surface to another. None is emitted today; this makes
    /// emitting one a visible change rather than a silent one.
    /// </summary>
    [Fact]
    public async Task A_narrowed_tools_listing_carries_no_shared_cache_hint()
    {
        using var factory = McpTestHost.CreateFactory(nameof(A_narrowed_tools_listing_carries_no_shared_cache_hint));
        var token = McpTestHost.MintToken(McpTestHost.ReadScope, "mcp:tool:expert_list");
        await using var client = await McpTestHost.ConnectAsync(factory, token);

        var result = await client.ListToolsAsync(new ModelContextProtocol.Protocol.ListToolsRequestParams());

        result.Tools.Select(t => t.Name).Should().Equal("expert_list");
        result.CacheScope.Should().NotBe(ModelContextProtocol.Protocol.CacheScope.Public);
        result.TimeToLive.Should().BeNull();
    }
}
