using System.ClientModel;
using ExpertToJob.Application.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ExpertToJob.Infrastructure.Embeddings;

/// <summary>
/// <see cref="IEmbedder"/> over an OpenAI-compatible <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>.
/// Logs the input-token count of every batch: embedding spend is an infra/operational cost, tracked
/// for visibility only — it is deliberately NOT charged against per-user token caps (see the RAG plan).
///
/// <para><b>Provider-neutral, and named so since EXP-64.</b> It was called <c>GeminiEmbedder</c>,
/// but nothing in it ever was Gemini-specific: the endpoint, the model and the breaker window all
/// arrive as constructor arguments, and everything vendor-shaped happens in the construction branch
/// that builds the generator (<see cref="EmbeddingServiceCollectionExtensions"/>). That is what lets
/// a second provider be an argument rather than a fork.</para>
///
/// <para>The provider arrives as a constructor argument for one reason beyond symmetry: it is half
/// of the <see cref="Tag"/> every vector is stamped with, and the tag is what makes mixing vectors
/// from two models impossible rather than merely unlikely (EXP-65).</para>
/// </summary>
public sealed class OpenAICompatibleEmbedder : IEmbedder
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly ILogger<OpenAICompatibleEmbedder> _logger;

    private readonly int _dimensions;
    private readonly TimeSpan _retryDelay;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _quotaBreakerWindow;

    private readonly object _breakerLock = new();
    private DateTimeOffset _quotaOpenUntil = DateTimeOffset.MinValue;

    public OpenAICompatibleEmbedder(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        EmbeddingsProvider provider,
        string model,
        int dimensions,
        ILogger<OpenAICompatibleEmbedder> logger,
        TimeSpan? retryDelay = null,
        TimeProvider? clock = null,
        TimeSpan? quotaBreakerWindow = null)
    {
        _generator = generator;
        Model = model;
        Tag = TagFor(provider, model);
        _dimensions = dimensions;
        _logger = logger;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(20);
        _clock = clock ?? TimeProvider.System;
        _quotaBreakerWindow = quotaBreakerWindow ?? TimeSpan.FromSeconds(1800);
    }

    public string Model { get; }

    /// <inheritdoc />
    public string Tag { get; }

    /// <summary>
    /// The one place a vector tag is spelled. Kept as a static so the search paths and the
    /// reconciler tests can form the same string without standing an embedder up, and so the
    /// separator exists once rather than in every call site that splits on it.
    /// </summary>
    public static string TagFor(EmbeddingsProvider provider, string model) => $"{provider}/{model}";

    public async Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
    {
        if (inputs.Count == 0)
        {
            return new EmbeddingBatch([], 0);
        }

        ThrowIfBreakerOpen();

        var options = new EmbeddingGenerationOptions { Dimensions = _dimensions };
        var generated = await GenerateWithRetryAsync(inputs, options, ct);
        var vectors = generated.Select(e => e.Vector.ToArray()).ToList();
        var inputTokens = generated.Usage?.InputTokenCount ?? 0;

        _logger.LogInformation(
            "embedding-index: embedded {InputCount} input(s) using {Model}, {InputTokens} input token(s)",
            inputs.Count, Model, inputTokens);

        return new EmbeddingBatch(vectors, inputTokens);
    }

    /// <summary>Providers throttle embedding requests per minute; a burst (reconciler
    /// backfill, eval corpus) trips 429s that clear on their own. Waits and retries a few times;
    /// a 429 that outlives the whole retry budget is the daily request cap, not a throttle, so it
    /// surfaces as <see cref="EmbeddingQuotaExceededException"/> for callers to back off on
    /// (P1T-98). Other errors surface to the caller's own failure handling untouched.</summary>
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
            catch (ClientResultException ex) when (ex.Status == 429)
            {
                if (attempt >= maxAttempts)
                {
                    OpenBreaker();
                    throw new EmbeddingQuotaExceededException(
                        $"Embedding quota for {Model} still exhausted after {maxAttempts} attempts.", ex);
                }

                var delay = _retryDelay * attempt;
                _logger.LogWarning(
                    "embedding-index: 429 from {Model}, attempt {Attempt}/{MaxAttempts}, waiting {DelaySeconds}s",
                    Model, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>While the breaker is open every embed call fails fast with the typed quota
    /// exception — no provider request is spent. The daily cap cannot clear within any short
    /// retry, so probing it just burns the next day's allowance (P1T-99).</summary>
    private void ThrowIfBreakerOpen()
    {
        lock (_breakerLock)
        {
            if (_clock.GetUtcNow() < _quotaOpenUntil)
            {
                throw new EmbeddingQuotaExceededException(
                    $"Embedding quota breaker for {Model} is open until {_quotaOpenUntil:O}.");
            }
        }
    }

    private void OpenBreaker()
    {
        lock (_breakerLock)
        {
            _quotaOpenUntil = _clock.GetUtcNow() + _quotaBreakerWindow;
        }

        _logger.LogWarning(
            "embedding-index: quota exhausted for {Model}; breaker open for {WindowSeconds}s",
            Model, _quotaBreakerWindow.TotalSeconds);
    }
}
