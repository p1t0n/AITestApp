using System.ClientModel;
using System.Globalization;
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
    /// (P1T-98). Other errors surface to the caller's own failure handling untouched.
    ///
    /// <para>How long to wait comes from the provider when the provider says
    /// (<see cref="RetryDelayFor"/>), and from the escalating fallback only when it does not.</para></summary>
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

                var delay = RetryDelayFor(ex, fallback: _retryDelay * attempt);
                _logger.LogWarning(
                    "embedding-index: 429 from {Model}, attempt {Attempt}/{MaxAttempts}, waiting {DelaySeconds}s",
                    Model, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>
    /// How long to wait after a 429, honouring what the provider asked for: <c>retry-after-ms</c>
    /// first, then <c>Retry-After</c>, then the caller's escalating fallback
    /// (<c>manuals/adr-embeddings-provider-seam.md</c> §2 decision 7). The order is the one the
    /// OpenAI-compatible endpoints actually use — <c>retry-after-ms</c> is the more precise of the
    /// two where both appear, and taking seconds when milliseconds were offered would round a
    /// 300&#160;ms throttle up to a full second on every attempt.
    ///
    /// <para><c>Retry-After</c> is also allowed to be an HTTP-date. Parsed, because a provider that
    /// sends one and gets the fallback instead is a wait nobody chose; an absolute time already in
    /// the past means "now", not a negative delay.</para>
    ///
    /// <para>A header that parses to a <em>negative</em> number is not a wait, it is nonsense, and
    /// is treated as such — the escalating fallback, which is a length somebody chose, rather than
    /// zero, which would turn a throttle into a busy retry loop. Only an HTTP-date that has already
    /// passed means "now", because that one is a real instant honestly described.</para>
    ///
    /// <para><b>Capped</b> at <see cref="MaxHonouredRetryAfter"/>. A provider is trusted to say how
    /// long its throttle is, not to hold a reconcile pass open for an hour: past the cap the wait is
    /// no longer a retry, and the breaker — which is bounded by the active provider's own
    /// <c>QuotaBreakerSeconds</c> and fails fast meanwhile — is the better place to be.</para>
    ///
    /// <para>Static and public so the precedence can be asserted without a test standing up a
    /// client, waiting out a real delay, or reading it back out of a log line.</para>
    /// </summary>
    public static TimeSpan RetryDelayFor(ClientResultException failure, TimeSpan fallback)
    {
        var headers = failure.GetRawResponse()?.Headers;
        if (headers is null)
        {
            return fallback;
        }

        if (headers.TryGetValue("retry-after-ms", out var afterMs)
            && double.TryParse(afterMs, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
            && ms >= 0)
        {
            return Cap(TimeSpan.FromMilliseconds(ms));
        }

        if (!headers.TryGetValue("Retry-After", out var retryAfter) || retryAfter is null)
        {
            return fallback;
        }

        if (double.TryParse(retryAfter, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0)
        {
            return Cap(TimeSpan.FromSeconds(seconds));
        }

        return DateTimeOffset.TryParse(
            retryAfter, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var until)
            ? Cap(until - DateTimeOffset.UtcNow)
            : fallback;
    }

    /// <summary>The longest wait a provider's own header is allowed to ask for — the same 60s the
    /// escalating fallback tops out at, so honouring a header can never wait longer than ignoring
    /// one would have.</summary>
    public static readonly TimeSpan MaxHonouredRetryAfter = TimeSpan.FromSeconds(60);

    private static TimeSpan Cap(TimeSpan asked)
        => asked < TimeSpan.Zero ? TimeSpan.Zero
            : asked > MaxHonouredRetryAfter ? MaxHonouredRetryAfter
            : asked;

    /// <summary>The window this embedder's quota breaker stays open for once retries are spent —
    /// the active provider's <c>QuotaBreakerSeconds</c>, which differs by an order of magnitude
    /// between a per-minute token cap and a daily request allowance. Exposed so a test can read the
    /// number a container actually built rather than the one it hoped for.</summary>
    public TimeSpan QuotaBreakerWindow => _quotaBreakerWindow;

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
