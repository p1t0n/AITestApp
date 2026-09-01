using FluentAssertions;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

public class McpServerSmokeTests
{
    [Fact]
    public async Task Request_without_bearer_token_is_rejected_with_401()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Request_without_bearer_token_is_rejected_with_401));
        var http = factory.CreateClient();

        var response = await http.PostAsync("/", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_client_can_list_tools_including_expert_list()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Authenticated_client_can_list_tools_including_expert_list));
        await using var client = await McpTestHost.ConnectAsync(factory);

        var tools = await client.ListToolsAsync();

        tools.Select(t => t.Name).Should().Contain("expert_list");
    }

    [Fact]
    public async Task Calling_expert_list_returns_the_seeded_expert()
    {
        using var factory = McpTestHost.CreateFactory(nameof(Calling_expert_list_returns_the_seeded_expert));
        McpTestHost.SeedExpert(factory);
        await using var client = await McpTestHost.ConnectAsync(factory);

        var result = await client.CallToolAsync("expert_list");

        result.IsError.Should().NotBe(true);
        var text = (result.StructuredContent?.ToString() ?? "")
            + string.Join("\n", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
        text.Should().Contain("Lovelace");
    }
}
