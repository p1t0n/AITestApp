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
/// The seam (EXP-16/EXP-17, <c>manuals/adr-chat-provider-seam.md</c> §1–§2):
/// <c>AddChatProvider</c> is the one place a chat provider is chosen, and everything
/// provider-specific happens in a construction branch that is unreachable when another provider is
/// configured. Both branches exist now, so each claim below is asserted on the branch it is about.
///
/// <para>Two properties are worth a test rather than a reading. First, that an unknown provider
/// name stops the host instead of quietly picking one — a typo'd discriminator is the failure mode
/// a string would hide and an enum makes loud. Second, that the shared decorator stack still wraps
/// <em>every</em> client the seam registers: the whole point of branching only around client
/// construction is that a later branch cannot bypass metering or the Runtime Budget, and "cannot"
/// is a claim that needed evidence before the Azure branch landed on top of it, and needs it no
/// less now that it has.</para>
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

    /// <summary>The keys the shipped settings files carry for each provider, so a test names only
    /// what it is actually about. Both blocks are handed to every registration below: configuration
    /// a deployment does not use is still configuration a deployment has, and a branch that read
    /// the wrong block would go unnoticed if only the active one were present.</summary>
    private static readonly (string, string?)[] BothProviderBlocks =
    [
        ("Ai:Gemini:Endpoint", "https://generativelanguage.googleapis.com/v1beta/openai"),
        ("Ai:Gemini:Model", "gemini-3.5-flash-lite"),
        ("Ai:Gemini:ApiKey", "test-key"),
        ("Ai:AzureFoundry:Endpoint", "https://experttojob-openai-swc.openai.azure.com/openai/v1/"),
        ("Ai:AzureFoundry:Model", "gpt-4-1-mini"),
        ("Ai:AzureFoundry:ApiKey", "test-key"),
    ];

    /// <summary>A provider registered the way the host registers it, on the named branch.</summary>
    private static IServiceProvider Build(string provider, params (string Key, string? Value)[] extra)
    {
        var services = new ServiceCollection();
        services.AddChatProvider(
            Config([(ProviderKey, provider), .. BothProviderBlocks, .. extra]));
        return services.BuildServiceProvider();
    }

    private static IServiceProvider BuildGemini(params (string Key, string? Value)[] extra) =>
        Build("Gemini", extra);

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

    /// <summary>
    /// The Azure branch's safety property, and the reason it is asserted rather than read off the
    /// diff: <b>the Gemini shims are absent</b> (EXP-17, ADR §2 decision 2 and §3). An absence is
    /// what regresses silently — a shim copied onto this branch by someone tidying the two branches
    /// into one would break nothing visible here and would send a thought-signature policy and a
    /// <c>finish_reason</c> rewriter at an endpoint that measurably needs neither.
    ///
    /// <para>Both shims are checked, not only the handler the name mentions: they are one decision
    /// ("no Gemini quirk reaches this client"), and each lives in a different half of the SDK
    /// pipeline, so each can arrive without the other.</para>
    /// </summary>
    [Fact]
    public void AzureProvider_ConstructsNoGeminiCompatHandler()
    {
        var client = Build("AzureFoundry").GetRequiredService<OpenAIClient>();

        HandlerTypesOf(client).Should().NotContain(typeof(GeminiCompatHandler),
            "the probe saw only finish_reason values the SDK already parses (ADR §3)");
        PolicyTypesOf(client).Should().NotContain(typeof(GeminiThoughtSignaturePolicy),
            "the probe replayed a tool-call history the endpoint accepted unmodified (ADR §3)");

        // The transport is the SDK's own, untouched: this branch passes no Transport at all, which
        // is the difference from Gemini that the two assertions above are downstream of.
        TransportOf(client).Should().BeSameAs(HttpClientPipelineTransport.Shared,
            "a branch that built its own transport would be a shim by another name");
    }

    /// <summary>
    /// Per-agent overrides are read from the <b>active provider's own block</b> and from no other
    /// (ADR §2 decision 10). Both blocks are configured here, each naming a different agent, so the
    /// test fails on either mistake: an active branch that missed its own overrides, and one that
    /// picked up the idle provider's.
    ///
    /// <para>The model each keyed client actually runs on is asserted too, because "a keyed client
    /// exists" is satisfied by a client on the wrong model. Under Azure that value is a
    /// <em>deployment</em> name — same key spelling as Gemini, different meaning (ADR §2
    /// decision 3).</para>
    /// </summary>
    [Theory]
    [InlineData("Gemini", "cv-tailoring", "gemini-pro-latest", "match")]
    [InlineData("AzureFoundry", "match", "gpt-4-1-nano", "cv-tailoring")]
    public void PerAgentOverride_ResolvesKeyedClient_ForBothProviders(
        string provider, string overridden, string model, string otherProvidersAgent)
    {
        var sp = Build(
            provider,
            ("Ai:Gemini:Agents:cv-tailoring", "gemini-pro-latest"),
            ("Ai:AzureFoundry:Agents:match", "gpt-4-1-nano"));

        var keyed = sp.GetRequiredKeyedService<IChatClient>(overridden);
        keyed.GetService<ChatClientMetadata>()!.DefaultModelId.Should().Be(
            model, $"'{overridden}' overrides its model inside the {provider} block");

        sp.GetKeyedService<IChatClient>(otherProvidersAgent).Should().BeNull(
            $"'{otherProvidersAgent}' is overridden in the idle provider's block, which {provider} "
            + "must not read");

        // The agent that inherits still gets a client, on the active provider's default model.
        sp.ResolveAgentChatClient(otherProvidersAgent).GetService<ChatClientMetadata>()!.DefaultModelId
            .Should().Be(provider == "Gemini" ? "gemini-3.5-flash-lite" : "gpt-4-1-mini");
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

    /// <summary>The transport the pipeline ends in: the Gemini branch builds its own so it can slot
    /// the compat handler in, every other branch gets the SDK's shared one.</summary>
    private static PipelineTransport TransportOf(OpenAIClient client) =>
        (PipelineTransport)typeof(ClientPipeline).GetField("_transport", Any)!
            .GetValue(PipelineOf(client))!;

    /// <summary>The transport's <see cref="HttpMessageHandler"/> chain, outermost first.</summary>
    private static IEnumerable<Type> HandlerTypesOf(OpenAIClient client)
    {
        var transport = TransportOf(client);
        transport.Should().BeOfType<HttpClientPipelineTransport>(
            "reading the handler chain at all depends on the transport being the HTTP one");

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
