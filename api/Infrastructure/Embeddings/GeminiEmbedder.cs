using System.ClientModel;
using CvManager.Application.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CvManager.Infrastructure.Embeddings;

/// <summary>
/// <see cref="IEmbedder"/> over an OpenAI-compatible <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>.
/// Logs the input-token count of every batch: embedding spend is an infra/operational cost, tracked
/// for visibility only — it is deliberately NOT charged against per-user token caps (see the RAG plan).
/// </summary>
public sealed class GeminiEmbedder : IEmbedder
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly ILogger<GeminiEmbedder> _logger;

    private readonly int _dimensions;

    public GeminiEmbedder(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        string model,
        int dimensions,
        ILogger<GeminiEmbedder> logger)
    {
        _generator = generator;
        Model = model;
        _dimensions = dimensions;
        _logger = logger;
    }

    public string Model { get; }

    public async Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
    {
        if (inputs.Count == 0)
        {
            return new EmbeddingBatch([], 0);
        }

        var options = new EmbeddingGenerationOptions { Dimensions = _dimensions };
        var generated = await GenerateWithRetryAsync(inputs, options, ct);
        var vectors = generated.Select(e => e.Vector.ToArray()).ToList();
        var inputTokens = generated.Usage?.InputTokenCount ?? 0;

        _logger.LogInformation(
            "embedding-index: embedded {InputCount} input(s) using {Model}, {InputTokens} input token(s)",
            inputs.Count, Model, inputTokens);

        return new EmbeddingBatch(vectors, inputTokens);
    }

    /// <summary>Gemini's free tier throttles embedding requests per minute; a burst (reconciler
    /// backfill, eval corpus) trips 429s that clear on their own. Waits and retries a few times
    /// before letting the error surface to the caller's own failure handling.</summary>
    private async Task<GeneratedEmbeddings<Embedding<float>>> GenerateWithRetryAsync(
        IReadOnlyList<string> inputs, EmbeddingGenerationOptions options, CancellationToken ct)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _generator.GenerateAsync(inputs, options, cancellationToken: ct);
            }
            catch (ClientResultException ex) when (ex.Status == 429 && attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(20 * attempt);
                _logger.LogWarning(
                    "embedding-index: 429 from {Model}, attempt {Attempt}/{MaxAttempts}, waiting {DelaySeconds}s",
                    Model, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }
}
