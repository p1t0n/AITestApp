using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EmployeeManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Endpoint tests for POST /agents/match. They run against the real host but swap the chat model
/// and the match tool source for fakes, so the host composes without a model key and no network is
/// touched — the focus is request validation and wiring, not the model.
/// </summary>
public class MatchEndpointTests
{
    private static WebApplicationFactory<Program> FakedHost() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(s =>
            {
                s.AddSingleton<IChatClient>(new FakeChatClient(
                    () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "Fit: MODERATE (60/100)"))));
                s.AddKeyedSingleton<EmployeeManager.Agents.Mcp.IMcpToolSource>(
                    "match", (_, _) => new FakeToolSource());
            }));

    [Fact]
    public async Task Returns_400_when_job_description_is_blank()
    {
        using var factory = FakedHost();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/agents/match", new { employeeId = Guid.NewGuid(), jobDescription = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_400_when_employee_id_is_empty()
    {
        using var factory = FakedHost();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/agents/match", new { employeeId = Guid.Empty, jobDescription = "Senior React engineer." });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_the_agents_answer_for_a_valid_request()
    {
        using var factory = FakedHost();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/agents/match", new { employeeId = Guid.NewGuid(), jobDescription = "Senior React engineer, GraphQL." });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("answer").GetString().Should().Be("Fit: MODERATE (60/100)");
    }
}
