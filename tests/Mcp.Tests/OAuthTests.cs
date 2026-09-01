using FluentAssertions;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

public class OAuthTests
{
    [Fact]
    public async Task Valid_token_with_read_scope_can_list_tools()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Valid_token_with_read_scope_can_list_tools));
        var token = McpTestHost.MintToken(McpTestHost.ReadScope);
        await using var client = await McpTestHost.ConnectAsync(factory, token);

        var tools = await client.ListToolsAsync();

        tools.Select(t => t.Name).Should().Contain("expert_list");
    }

    [Fact]
    public async Task Protected_resource_metadata_advertises_auth_server_and_scopes()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Protected_resource_metadata_advertises_auth_server_and_scopes));
        var http = factory.CreateClient();

        var body = await http.GetStringAsync("/.well-known/oauth-protected-resource");

        body.Should().Contain("authorization_servers");
        body.Should().Contain("realms/expert-to-job");
        body.Should().Contain("mcp:read").And.Contain("mcp:write").And.Contain("mcp:admin");
    }

    [Fact]
    public async Task Read_scope_token_cannot_see_destructive_tools()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Read_scope_token_cannot_see_destructive_tools));
        var token = McpTestHost.MintToken(McpTestHost.ReadScope);
        await using var client = await McpTestHost.ConnectAsync(factory, token);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToList();

        names.Should().Contain("expert_list");        // read is allowed
        names.Should().NotContain("expert_delete");   // admin scope required
    }

    [Fact]
    public async Task Admin_scope_token_can_see_destructive_tools()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Admin_scope_token_can_see_destructive_tools));
        var token = McpTestHost.MintToken(McpTestHost.ReadScope, McpTestHost.AdminScope);
        await using var client = await McpTestHost.ConnectAsync(factory, token);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToList();

        names.Should().Contain("expert_delete");
    }

    [Fact]
    public async Task Token_with_wrong_audience_is_rejected()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Token_with_wrong_audience_is_rejected));
        var token = McpTestHost.MintTokenFor(McpTestHost.Issuer, "https://not-this-server", McpTestHost.ReadScope);
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await http.PostAsync("/",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }
}
