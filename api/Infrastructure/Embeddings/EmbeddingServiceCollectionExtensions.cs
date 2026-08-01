using System.ClientModel;
using CvManager.Application.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace CvManager.Infrastructure.Embeddings;

/// <summary>
/// Registers the embedding backend for semantic roster search. Opt-in (not part of
/// <c>AddInfrastructure</c>): only the MCP service, which runs the reconciliation worker and the
/// semantic search query, calls this — the Web API has no need to embed.
/// </summary>
public static class EmbeddingServiceCollectionExtensions
{
    public static IServiceCollection AddGeminiEmbeddings(
        this IServiceCollection services, IConfiguration config)
    {
        var cfg = config.GetSection(EmbeddingOptions.Section).Get<EmbeddingOptions>()
                  ?? new EmbeddingOptions();

        // One OpenAI-compatible embedding client (endpoint + credential), same shape as the chat client.
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
        {
            var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") is { Length: > 0 } envToken
                ? envToken
                : cfg.ApiKey;
            var client = new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(cfg.Endpoint) });
            return client.GetEmbeddingClient(cfg.EmbeddingModel).AsIEmbeddingGenerator();
        });

        services.AddSingleton<IEmbedder>(sp => new GeminiEmbedder(
            sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            cfg.EmbeddingModel,
            cfg.Dimensions,
            sp.GetRequiredService<ILogger<GeminiEmbedder>>()));

        return services;
    }
}
