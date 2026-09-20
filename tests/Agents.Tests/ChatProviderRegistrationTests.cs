using System.ClientModel.Primitives;
using System.Reflection;
using ExpertToJob.Agents.Configuration;
using ExpertToJob.Agents.Usage;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The seam (EXP-16, <c>manuals/adr-chat-provider-seam.md</c> §1–§2): <c>AddChatProvider</c> is the
/// one place a chat provider is chosen, and everything provider-specific happens in a construction
/// branch that is unreachable when another provider is configured.
///
/// <para>Two properties are worth a test rather than a reading. First, that an unknown provider
/// name stops the host instead of quietly picking one — a typo'd discriminator is the failure mode
/// a string would hide and an enum makes loud. Second, that the shared decorator stack still wraps
/// <em>every</em> client the seam registers: the whole point of branching only around client
/// construction is that a later branch cannot bypass metering or the Runtime Budget, and "cannot"
/// is a claim that needs evidence before the Azure branch lands on top of it (EXP-17).</para>
///
/// <para>Everything here is structural: the pipeline is read off the constructed client, never
/// exercised against a model. No test in this file reaches the network.</para>
/// </summary>
public class ChatProviderRegistrationTests
{
    private const string ProviderKey = "Ai:Chat:Provider";

    private static IConfiguration Config(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value))
            .Build();

    /// <summary>A provider registered the way the host registers it, on the incumbent branch.</summary>
    private static IServiceProvider BuildGemini(params (string Key, string? Value)[] extra)
    {
        var settings = new (string, string?)[]
        {
            (ProviderKey, "Gemini"),
            ("Ai:Gemini:Endpoint", "https://generativelanguage.googleapis.com/v1beta/openai"),
            ("Ai:Gemini:Model", "gemini-3.5-flash-lite"),
            ("Ai:Gemini:ApiKey", "test-key"),
        };

        var services = new ServiceCollection();
        services.AddChatProvider(Config([.. settings, .. extra]));
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Vertex")]
    [InlineData("gemini-3.5-flash-lite")]          // a model id where a provider name belongs
    [InlineData("7")]                              // binds to the underlying type, names no member
    [InlineData("Gemini,AzureFoundry")]            // parses as a flags combination; this is not one
    public void UnknownProvider_ThrowsAtStartup(string provider)
    {
        var act = () => new ServiceCollection().AddChatProvider(Config((ProviderKey, provider)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{provider}*", "the throw has to quote the value that was rejected")
            .WithMessage($"*{ProviderKey}*", "and the key it came from, so the fix is one edit away");

        // "In every environment" (ADR §2 decision 6) is a property of the signature, not of a
        // branch inside it: the seam's only input is configuration, so there is no environment in
        // which this registration could succeed. Missing-credential failure stays Production-only
        // and is a different guard, in api/Agents/Program.cs.
        typeof(ChatProviderServiceCollectionExtensions)
            .GetMethod(nameof(ChatProviderServiceCollectionExtensions.AddChatProvider))!
            .GetParameters().Select(p => p.ParameterType)
            .Should().Equal([typeof(IServiceCollection), typeof(IConfiguration)],
                "a seam that took an IHostEnvironment could make a typo acceptable somewhere");
    }

    /// <summary>The other enum member is known but unbuilt, and says so. A different exception type
    /// from the unknown-name throw on purpose: "you named something real that this build cannot do
    /// yet" is a different mistake from "you named nothing at all", and only one of them is fixed by
    /// correcting a typo.</summary>
    [Fact]
    public void AzureFoundryProvider_ThrowsUntilItsBranchLands()
    {
        var act = () => new ServiceCollection().AddChatProvider(Config((ProviderKey, "AzureFoundry")));

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*EXP-17*", "the message has to name the ticket that builds the branch");
    }

    /// <summary>The usage row records which backend served the run (EXP-19), so the seam has to
    /// hand the resolved choice to the container — the alternative, re-reading the discriminator
    /// where the row is written, would be a second place a provider name is interpreted.</summary>
    [Fact]
    public void TheActiveProvider_IsResolvableForTheUsageRow()
    {
        BuildGemini().GetRequiredService<ChatProvider>().Should().Be(ChatProvider.Gemini);
    }

    [Fact]
    public void GeminiProvider_AttachesCompatHandlerAndSignaturePolicy()
    {
        var client = BuildGemini().GetRequiredService<OpenAIClient>();

        PolicyTypesOf(client).Should().Contain(typeof(GeminiThoughtSignaturePolicy),
            "the request-side thought-signature shim is attached per call on this branch");
        HandlerTypesOf(client).Should().Contain(typeof(GeminiCompatHandler),
            "the response-side finish_reason shim sits in the transport's handler chain");
    }

    [Fact]
    public void EveryClient_IsWrappedInMeteringAndBudget()
    {
        var sp = BuildGemini(
            ("Ai:Gemini:Agents:cv-tailoring", "gemini-pro-latest"),
            ("Ai:Gemini:Agents:match", "gemini-flash-latest"));

        // What the container hands out: the default client and every keyed per-agent client.
        sp.GetRequiredService<IChatClient>().Should().BeOfType<MeteringChatClient>();
        foreach (var agent in new[] { "cv-tailoring", "match" })
        {
            sp.GetRequiredKeyedService<IChatClient>(agent).Should().BeOfType<MeteringChatClient>(
                $"the keyed client for '{agent}' is registered by the same provider-neutral loop");
        }

        // And what an agent actually resolves: the budget on the outside, metering underneath.
        // "roster-qa" overrides nothing and so falls through to the default client — the path a
        // new agent gets without anyone wiring it.
        foreach (var agent in new[] { "cv-tailoring", "match", "roster-qa" })
        {
            var resolved = sp.ResolveAgentChatClient(agent);

            resolved.Should().BeOfType<RuntimeBudgetChatClient>(
                $"'{agent}' cannot opt out of its per-run ceiling");
            resolved.GetService(typeof(MeteringChatClient)).Should().BeOfType<MeteringChatClient>(
                $"'{agent}' cannot opt out of metering either");
        }
    }

    private const BindingFlags Any =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// The pipeline the SDK built, read off the client. It is reached by reflection because
    /// <c>ClientPipeline</c> deliberately exposes no policy list — the alternative was to assert
    /// the shims by calling a model, which is the one thing the ticket rules out. A field rename in
    /// the SDK breaks this loudly, which is the right failure: the claim being made is about the
    /// object that ships, not about the options we handed the constructor.
    /// </summary>
    private static ClientPipeline PipelineOf(OpenAIClient client) =>
        (ClientPipeline)typeof(OpenAIClient).GetProperty("Pipeline", Any)!.GetValue(client)!;

    private static IEnumerable<Type> PolicyTypesOf(OpenAIClient client)
    {
        var pipeline = PipelineOf(client);
        var policies = (ReadOnlyMemory<PipelinePolicy>)typeof(ClientPipeline)
            .GetField("_policies", Any)!.GetValue(pipeline)!;
        return policies.ToArray().Select(p => p.GetType());
    }

    /// <summary>The transport's <see cref="HttpMessageHandler"/> chain, outermost first.</summary>
    private static IEnumerable<Type> HandlerTypesOf(OpenAIClient client)
    {
        var transport = typeof(ClientPipeline).GetField("_transport", Any)!.GetValue(PipelineOf(client));
        transport.Should().BeOfType<HttpClientPipelineTransport>(
            "the Gemini branch builds its own transport so it can slot the compat handler in");

        var httpClient = (HttpClient)typeof(HttpClientPipelineTransport)
            .GetField("_httpClient", Any)!.GetValue(transport)!;
        var handler = (HttpMessageHandler?)typeof(HttpMessageInvoker)
            .GetField("_handler", Any)!.GetValue(httpClient);

        while (handler is not null)
        {
            yield return handler.GetType();
            handler = (handler as DelegatingHandler)?.InnerHandler;
        }
    }
}
