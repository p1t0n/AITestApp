using CvManager.Agents.Configuration;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CvManager.Agents.Tests;

/// <summary>
/// Tests per-agent model selection: a shared default chat client, plus a keyed override per agent
/// configured under Gemini:Agents. Asserted through the chat client's reported model id —
/// no live model call (constructing the client and reading its metadata touches no network).
/// </summary>
public class ModelSelectionTests
{
    private static IServiceProvider BuildProvider(string defaultModel, params (string Agent, string Model)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Gemini:Endpoint"] = "https://generativelanguage.googleapis.com/v1beta/openai",
            ["Gemini:Model"] = defaultModel,
            ["Gemini:ApiKey"] = "test-key",
        };
        foreach (var (agent, model) in overrides)
        {
            settings[$"Gemini:Agents:{agent}"] = model;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddGeminiChatClient(config);
        return services.BuildServiceProvider();
    }

    private static string? ModelOf(IChatClient client) =>
        (client.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata)?.DefaultModelId;

    [Fact]
    public void Default_chat_client_uses_the_configured_default_model()
    {
        var sp = BuildProvider("gemini-flash-lite-latest");

        var client = sp.GetRequiredService<IChatClient>();

        ModelOf(client).Should().Be("gemini-flash-lite-latest");
    }

    [Fact]
    public void An_overridden_agent_resolves_its_own_model_others_fall_back_to_default()
    {
        var sp = BuildProvider("gemini-flash-lite-latest", ("cv-tailoring", "gemini-pro-latest"));

        ModelOf(sp.ResolveAgentChatClient("cv-tailoring")).Should().Be("gemini-pro-latest");
        ModelOf(sp.ResolveAgentChatClient("roster-qa")).Should().Be("gemini-flash-lite-latest");
    }

    [Fact]
    public void Without_overrides_every_agent_resolves_the_shared_default_client()
    {
        var sp = BuildProvider("gemini-flash-lite-latest");

        var a = sp.ResolveAgentChatClient("cv-tailoring");
        var b = sp.ResolveAgentChatClient("match");

        a.Should().BeSameAs(b, "no overrides means everyone shares the one default client");
        ModelOf(a).Should().Be("gemini-flash-lite-latest");
    }
}
