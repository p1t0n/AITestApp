using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace CvManager.Agents.Configuration;

/// <summary>
/// Registers the Gemini chat clients: one shared default <see cref="IChatClient"/> on the
/// configured default model, plus a keyed <see cref="IChatClient"/> for each agent that declares a
/// per-agent model override under <c>Gemini:Agents</c>. All clients share a single
/// <see cref="OpenAIClient"/> (endpoint + credential); only the model id differs.
/// </summary>
public static class GeminiServiceCollectionExtensions
{
    public static IServiceCollection AddGeminiChatClient(
        this IServiceCollection services, IConfiguration config)
    {
        var cfg = config.GetSection(GeminiOptions.Section).Get<GeminiOptions>()
                  ?? new GeminiOptions();

        // One OpenAI-compatible client (endpoint + key) shared by every model; per-agent clients
        // differ only in the model id passed to GetChatClient.
        services.AddSingleton(_ =>
        {
            var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") is { Length: > 0 } envToken
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

        // Default chat client: the model everyone uses unless overridden.
        services.AddSingleton<IChatClient>(sp =>
            sp.GetRequiredService<OpenAIClient>().GetChatClient(cfg.Model).AsIChatClient());

        // One keyed client per agent that overrides the model.
        foreach (var (agentKey, model) in cfg.Agents)
        {
            services.AddKeyedSingleton<IChatClient>(agentKey, (sp, _) =>
                sp.GetRequiredService<OpenAIClient>().GetChatClient(model).AsIChatClient());
        }

        return services;
    }

    /// <summary>Resolves the chat client for an agent: its keyed model override if one is
    /// registered, otherwise the shared default client.</summary>
    public static IChatClient ResolveAgentChatClient(this IServiceProvider sp, string agentKey)
        => sp.GetKeyedService<IChatClient>(agentKey) ?? sp.GetRequiredService<IChatClient>();
}
