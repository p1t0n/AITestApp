using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;

namespace ExpertToJob.Agents.Configuration;

/// <summary>
/// <b>The seam</b> (<c>manuals/adr-chat-provider-seam.md</c> §1): the one place in the repo where a
/// chat provider is chosen. It registers one shared default <see cref="IChatClient"/> on the
/// configured default model, plus a keyed <see cref="IChatClient"/> for each agent that declares a
/// per-agent model override inside the active provider's own block. All clients share a single
/// <see cref="OpenAIClient"/> (endpoint + credential); only the model id differs.
///
/// <para>Everything provider-specific happens in the <b>construction branch</b> — the few lines
/// that build that <see cref="OpenAIClient"/>. Everything after it is provider-neutral: it holds an
/// <see cref="IChatClient"/> and cannot tell who is behind it. That is why Gemini's two shims are
/// attached inside a branch another provider never reaches, rather than kept out of its way by a
/// second factory: structural exclusion is the property that mattered, and one factory keeps the
/// keyed-per-agent registration — the piece every provider needs identically — written once
/// (ADR §2 decision 2).</para>
/// </summary>
public static class ChatProviderServiceCollectionExtensions
{
    /// <summary>The discriminator. Chat-only, because embeddings still call Google whatever chat
    /// does (ADR §2 decision 3).</summary>
    public const string ProviderKey = "Ai:Chat:Provider";

    /// <summary>The configuration section that used to hold these keys. Named once, here, because
    /// the guard below is the only thing left in the repo that may spell it.</summary>
    private const string LegacySection = "Gemini";

    /// <summary>What a construction branch hands back to the provider-neutral half: which model the
    /// default client runs on, and the per-agent overrides to register keyed clients for. Both come
    /// out of the active provider's own block — per-agent overrides never live in a shared one
    /// (ADR §2 decision 10) — and neither carries any trace of which provider produced it.</summary>
    private readonly record struct ChatModels(string Default, IReadOnlyDictionary<string, string> Agents);

    public static IServiceCollection AddChatProvider(
        this IServiceCollection services, IConfiguration config)
    {
        // A stale top-level section fails loudly rather than binding to nothing (ADR §2 decision 5).
        // There is no deprecation window: nothing outside this repo consumes this configuration, and
        // a silently-ignored key is the exact failure mode the rename was worth doing to prevent —
        // the host would start, every option would fall back to its property default, and the first
        // symptom would be a model nobody chose answering on a key nobody set.
        if (config.GetSection(LegacySection).Exists())
        {
            throw new InvalidOperationException(
                $"Configuration still carries a top-level '{LegacySection}' section. These keys moved "
                + $"to '{GeminiOptions.Section}' (e.g. '{GeminiOptions.Section}:Model', "
                + $"'{GeminiOptions.Section}:ApiKey'), with the chat provider named by "
                + $"'{ProviderKey}'. The GEMINI_API_KEY environment variable is unchanged. "
                + "See manuals/adr-chat-provider-seam.md.");
        }

        var provider = ReadProvider(config);

        // The one fact downstream code is allowed to know about the choice: its name, for the usage
        // row (EXP-19, ADR §2 decision 11). Registered as the enum rather than the string so a
        // reader still fails at the config edge; the string exists only in the database column.
        services.AddSingleton(typeof(ChatProvider), provider);

        // ---- the construction branch: the only code below that knows a provider's name ----
        var models = provider switch
        {
            ChatProvider.Gemini => AddGeminiClient(services, config),
            ChatProvider.AzureFoundry => AddAzureFoundryClient(services, config),
            var unreachable => throw new InvalidOperationException(
                $"'{ProviderKey}' bound to {unreachable}, which no construction branch builds. A new "
                + $"{nameof(ChatProvider)} member needs a branch here."),
        };

        // ---- provider-neutral from here down ----

        // Every client is wrapped with OpenTelemetryChatClient (gen_ai spans + token/duration
        // metrics, P1T-94) plus the MeteringChatClient (real model id + latency into the ambient
        // per-run scope, P1T-95). Both no-op when nothing listens; sensitive capture stays off.
        static IChatClient Instrument(IServiceProvider sp, IChatClient inner) =>
            new Usage.MeteringChatClient(
                inner.AsBuilder()
                    .UseOpenTelemetry(sp.GetService<ILoggerFactory>())
                    .Build());

        // Default chat client: the model everyone uses unless overridden.
        services.AddSingleton<IChatClient>(sp => Instrument(
            sp, sp.GetRequiredService<OpenAIClient>().GetChatClient(models.Default).AsIChatClient()));

        // One keyed client per agent that overrides the model.
        foreach (var (agentKey, model) in models.Agents)
        {
            services.AddKeyedSingleton<IChatClient>(agentKey, (sp, _) => Instrument(
                sp, sp.GetRequiredService<OpenAIClient>().GetChatClient(model).AsIChatClient()));
        }

        return services;
    }

    /// <summary>
    /// Reads the discriminator. An unknown name throws here, at startup, in <b>every</b>
    /// environment — a typo'd provider is wrong everywhere, unlike a missing credential, which
    /// stays a normal dev condition and is guarded Production-only in <c>api/Agents/Program.cs</c>
    /// (ADR §2 decision 6).
    ///
    /// <para>The value has to <b>name a member</b>, which is a stricter test than parsing as one.
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> also accepts a bare number, and
    /// it accepts a comma-separated list as a flags combination even for an enum that carries no
    /// <see cref="FlagsAttribute"/> — <c>"Gemini,AzureFoundry"</c> ORs to 1 and arrives as a
    /// perfectly <see cref="Enum.IsDefined{TEnum}(TEnum)"/> <c>AzureFoundry</c>. Two providers
    /// asked for and one silently chosen is the failure this guard exists to prevent, so the match
    /// is against the names themselves.</para>
    ///
    /// <para>An absent key means the incumbent. The shipped settings files name it explicitly, so
    /// the default is what a bare <see cref="ServiceCollection"/> in a test gets, not something a
    /// deployment relies on.</para>
    ///
    /// <para>Public because <see cref="ChatProviderStartupGuard"/> needs the same answer to know
    /// whose credential to require (EXP-18), and a second parse of the discriminator is a second
    /// thing to keep in agreement with this one.</para>
    /// </summary>
    public static ChatProvider ReadProvider(IConfiguration config)
    {
        var configured = config[ProviderKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return ChatProvider.Gemini;
        }

        var named = Enum.GetNames<ChatProvider>().FirstOrDefault(
            name => string.Equals(name, configured.Trim(), StringComparison.OrdinalIgnoreCase));

        return named is null
            ? throw new InvalidOperationException(
                $"'{ProviderKey}' is '{configured}', which is not a chat provider this build knows. "
                + $"Valid values: {string.Join(", ", Enum.GetNames<ChatProvider>())}. "
                + "See manuals/adr-chat-provider-seam.md.")
            : Enum.Parse<ChatProvider>(named);
    }

    /// <summary>
    /// The Gemini construction branch: one OpenAI-compatible client (endpoint + key) shared by every
    /// model, carrying the two shims this endpoint needs. Per-agent clients differ only in the model
    /// id passed to <c>GetChatClient</c>, which is why they are registered by the shared loop and
    /// not here.
    ///
    /// <para>The ServiceDefaults resilience handler is deliberately <b>not</b> attached, on this or
    /// any branch (ADR §2 decision 7): its 10 s per-attempt timeout cancels healthy tool-calling
    /// completions, and its retries stack on the SDK's own.</para>
    /// </summary>
    private static ChatModels AddGeminiClient(IServiceCollection services, IConfiguration config)
    {
        var cfg = config.GetSection(GeminiOptions.Section).Get<GeminiOptions>() ?? new GeminiOptions();

        services.AddSingleton(_ =>
        {
            var apiKey = Environment.GetEnvironmentVariable(GeminiOptions.ApiKeyVariable) is { Length: > 0 } envToken
                ? envToken
                : cfg.ApiKey;
            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri(cfg.Endpoint),
                // Response-side shim: retry malformed function calls, normalize nonstandard
                // finish_reason values the OpenAI SDK cannot parse.
                Transport = new HttpClientPipelineTransport(
                    new HttpClient(new GeminiCompatHandler(new HttpClientHandler()))),
            };
            options.AddPolicy(new GeminiThoughtSignaturePolicy(), PipelinePosition.PerCall);
            return new OpenAIClient(new ApiKeyCredential(apiKey), options);
        });

        return new ChatModels(cfg.Model, cfg.Agents);
    }

    /// <summary>
    /// The Azure construction branch (EXP-17): the plain OpenAI SDK pointed at the resource's
    /// <c>openai/v1/</c> endpoint, with the <b>deployment</b> name passed where a model id goes
    /// (ADR §2 decision 1). Not <c>Azure.AI.OpenAI</c>, whose newest release is a prerelease that
    /// recommends this package in its own notes, and whose <c>AzureOpenAIClient</c> derives from
    /// <see cref="OpenAIClient"/> and hands back the identical chat client anyway.
    ///
    /// <para><b>No transport handler and no per-call policy here, and that is the point.</b>
    /// Neither Gemini shim has an analog on this endpoint, and each absence was measured rather
    /// than assumed (ADR §3, <c>tests/Agents.Tests/AzureFoundryDialectProbeTests.cs</c>): a
    /// replayed tool-call history — the exact request Gemini 400s without
    /// <see cref="GeminiThoughtSignaturePolicy"/> — was accepted unmodified, and every
    /// <c>finish_reason</c> came back on a value the SDK already parses, so there is nothing for a
    /// <see cref="GeminiCompatHandler"/> analog to normalize. <see cref="ApiKeyCredential"/> sends
    /// <c>Authorization: Bearer</c> and the v1 endpoint accepts it, so not a header shim either.
    /// <c>ChatProviderRegistrationTests.AzureProvider_ConstructsNoGeminiCompatHandler</c> asserts
    /// the absence, because an absence is what regresses quietly.</para>
    /// </summary>
    private static ChatModels AddAzureFoundryClient(IServiceCollection services, IConfiguration config)
    {
        var cfg = config.GetSection(AzureFoundryOptions.Section).Get<AzureFoundryOptions>()
                  ?? new AzureFoundryOptions();

        services.AddSingleton(_ =>
        {
            // Env var first, config second — the same explicit read the Gemini branch does for
            // GEMINI_API_KEY, and for the same reason: a credential is a name read on purpose, not
            // a configuration path bound by the options system (ADR §2 decision 4).
            var apiKey = Environment.GetEnvironmentVariable(AzureFoundryOptions.ApiKeyVariable) is { Length: > 0 } envToken
                ? envToken
                : cfg.ApiKey;
            return new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(cfg.Endpoint) });
        });

        // cfg.Model and cfg.Agents carry DEPLOYMENT names on this provider. The shared loop below
        // passes them to GetChatClient exactly where Gemini's model ids go, which is the whole
        // reason the override dictionary was not forked into two shapes (ADR §2 decision 3).
        return new ChatModels(cfg.Model, cfg.Agents);
    }

    /// <summary>Resolves the chat client for an agent: its keyed model override if one is
    /// registered, otherwise the shared default client — wrapped in that agent's Runtime Budget
    /// (P1T-147).
    /// <para>This is the one place every agent asks for a model, which is why the budget hangs
    /// here: an agent cannot opt out of its ceiling, and a new agent inherits the default without
    /// anyone remembering to wire it. The wrapper is per-agent (the budget differs); the run state
    /// it spends against is ambient, so sharing one inner client across agents is safe.</para>
    /// </summary>
    public static IChatClient ResolveAgentChatClient(this IServiceProvider sp, string agentKey)
    {
        var client = sp.GetKeyedService<IChatClient>(agentKey) ?? sp.GetRequiredService<IChatClient>();
        var budgets = sp.GetService<IOptions<Usage.AgentBudgetOptions>>()?.Value
                      ?? new Usage.AgentBudgetOptions();
        return new Usage.RuntimeBudgetChatClient(
            client, agentKey, budgets.For(agentKey),
            sp.GetService<ILoggerFactory>()?.CreateLogger<Usage.RuntimeBudgetChatClient>());
    }
}
