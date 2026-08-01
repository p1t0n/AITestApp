using CvManager.Application.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CvManager.Infrastructure.Embeddings;

/// <summary>
/// <see cref="IEmbedder"/> over an OpenAI-compatible <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>.
/// Logs the input-token count of every batch: embedding spend is an infra/operational cost, tracked
/// for visibility only — it is deliberately NOT charged against per-user token caps (see the RAG plan).
/// </summary>
public sealed class GitHubModelsEmbedder : IEmbedder
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly ILogger<GitHubModelsEmbedder> _logger;

    public GitHubModelsEmbedder(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        string model,
        ILogger<GitHubModelsEmbedder> logger)
    {
        _generator = generator;
        Model = model;
        _logger = logger;
    }

    public string Model { get; }

    public async Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
    {
        if (inputs.Count == 0)
        {
            return new EmbeddingBatch([], 0);
        }

        var generated = await _generator.GenerateAsync(inputs, cancellationToken: ct);
        var vectors = generated.Select(e => e.Vector.ToArray()).ToList();
        var inputTokens = generated.Usage?.InputTokenCount ?? 0;

        _logger.LogInformation(
            "embedding-index: embedded {InputCount} input(s) using {Model}, {InputTokens} input token(s)",
            inputs.Count, Model, inputTokens);

        return new EmbeddingBatch(vectors, inputTokens);
    }
}
