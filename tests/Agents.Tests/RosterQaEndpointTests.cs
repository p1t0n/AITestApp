using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpertToJob.Agents.Tests.Fakes;
using ExpertToJob.Agents.Usage;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// POST /agents/roster-qa's response contract, at the host. The threading half lives in
/// <c>RosterQaThreadedTests</c> (agent + store, no HTTP); what is asserted here is the shape the
/// SPA actually parses.
///
/// <para>The chat client is faked <b>inside a real <see cref="MeteringChatClient"/></b> rather than
/// on its own, which is the only way this test means anything: the model id is not something the
/// agent knows: it is read off the response by the metering seam (P1T-95) and handed back through
/// the ambient run scope. A bare fake would report <c>null</c> for every case and the "carries the
/// model" test would pass for the wrong reason.</para>
/// </summary>
public class RosterQaEndpointTests
{
    private static WebApplicationFactory<Program> FakedHost(string? reportedModel) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.AddSingleton<IChatClient>(new MeteringChatClient(new FakeChatClient(
                    () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ada Lovelace knows React."))
                    {
                        ModelId = reportedModel,
                    })));
                s.AddKeyedSingleton<ExpertToJob.Agents.Mcp.IMcpToolSource>(
                    "roster-qa", (_, _) => new FakeToolSource());
                s.AddInMemoryAppDb("roster-qa");
            }));

    private static async Task<JsonElement> Ask(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync("/agents/roster-qa", new { question = "Who knows React?" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Reports_the_model_the_provider_said_answered()
    {
        using var factory = FakedHost("gemini-3.5-flash-lite-002");

        var body = await Ask(factory);

        body.GetProperty("answer").GetString().Should().Be("Ada Lovelace knows React.");
        body.GetProperty("threadId").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("modelId").GetString().Should().Be("gemini-3.5-flash-lite-002",
            "the answer names who wrote it — the id off the wire, not the configured one");
    }

    [Fact]
    public async Task Reports_no_model_when_the_provider_named_none()
    {
        using var factory = FakedHost(reportedModel: null);

        var body = await Ask(factory);

        // Null rather than absent or empty: the SPA's caption hangs off this being falsy, and an
        // empty string would render an empty caption instead of no caption.
        body.GetProperty("modelId").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("answer").GetString().Should().Be("Ada Lovelace knows React.");
    }
}
