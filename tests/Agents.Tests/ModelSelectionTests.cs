using EmployeeManager.Agents.Configuration;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EmployeeManager.Agents.Tests;

/// <summary>
/// Tests per-agent model selection: a shared default chat client, plus a keyed override per agent
/// configured under GitHubModels:Agents. Asserted through the chat client's reported model id —
/// no live model call (constructing the client and reading its metadata touches no network).
/// </summary>
public class ModelSelectionTests
{
    private static IServiceProvider BuildProvider(string defaultModel, params (string Agent, string Model)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["GitHubModels:Endpoint"] = "https://models.github.ai/inference",
            ["GitHubModels:Model"] = defaultModel,
            ["GitHubModels:ApiKey"] = "test-key",
        };
        foreach (var (agent, model) in overrides)
        {
            settings[$"GitHubModels:Agents:{agent}"] = model;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddGitHubModelsChatClient(config);
        return services.BuildServiceProvider();
    }

    private static string? ModelOf(IChatClient client) =>
        (client.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata)?.DefaultModelId;

    [Fact]
    public void Default_chat_client_uses_the_configured_default_model()
    {
        var sp = BuildProvider("openai/gpt-4o-mini");

        var client = sp.GetRequiredService<IChatClient>();

        ModelOf(client).Should().Be("openai/gpt-4o-mini");
    }

    [Fact]
    public void An_overridden_agent_resolves_its_own_model_others_fall_back_to_default()
    {
        var sp = BuildProvider("openai/gpt-4o-mini", ("cv-tailoring", "openai/gpt-4o"));

        ModelOf(sp.ResolveAgentChatClient("cv-tailoring")).Should().Be("openai/gpt-4o");
        ModelOf(sp.ResolveAgentChatClient("roster-qa")).Should().Be("openai/gpt-4o-mini");
    }

    [Fact]
    public void Without_overrides_every_agent_resolves_the_shared_default_client()
    {
        var sp = BuildProvider("openai/gpt-4o-mini");

        var a = sp.ResolveAgentChatClient("cv-tailoring");
        var b = sp.ResolveAgentChatClient("match");

        a.Should().BeSameAs(b, "no overrides means everyone shares the one default client");
        ModelOf(a).Should().Be("openai/gpt-4o-mini");
    }
}
