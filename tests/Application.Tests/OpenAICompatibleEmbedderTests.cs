using System.ClientModel;
using System.ClientModel.Primitives;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// Unit tests for <see cref="OpenAICompatibleEmbedder"/> using a deterministic fake generator — no
/// network. Verifies the batch shape, the reported token count, that spend is logged (embedding
/// cost is tracked for visibility, deliberately not charged to per-user caps), and how a 429 is
/// waited out.
/// </summary>
public class OpenAICompatibleEmbedderTests
{
    [Fact]
    public async Task Embeds_batch_preserving_order_and_reports_tokens()
    {
        var logger = new CapturingLogger<OpenAICompatibleEmbedder>();
        var generator = new FakeEmbeddingGenerator(dimensions: 1536, inputTokens: 42);
        var embedder = new OpenAICompatibleEmbedder(generator, EmbeddingsProvider.Gemini, "gemini-embedding-001", 1536, logger);

        var batch = await embedder.EmbedAsync(["alpha", "beta"]);

        batch.Vectors.Should().HaveCount(2);
        batch.Vectors[0].Should().HaveCount(1536);
        batch.InputTokens.Should().Be(42);
        embedder.Model.Should().Be("gemini-embedding-001");
        // gemini-embedding-001 defaults to 3072 dims; the vector(1536) column depends on this option.
        generator.LastOptions!.Dimensions.Should().Be(1536);
    }

    [Fact]
    public void Tags_its_vectors_with_provider_and_model()
    {
        // The model alone would not do: on Azure it is a deployment name somebody chooses, so two
        // providers can honestly report the same one (EXP-65, ADR §2 decision 9).
        var embedder = new OpenAICompatibleEmbedder(
            new FakeEmbeddingGenerator(), EmbeddingsProvider.AzureFoundry, "text-embedding-3-small",
            1536, new CapturingLogger<OpenAICompatibleEmbedder>());

        embedder.Model.Should().Be("text-embedding-3-small");
        embedder.Tag.Should().Be("AzureFoundry/text-embedding-3-small");
    }

    [Fact]
    public async Task Logs_token_count_and_model()
    {
        var logger = new CapturingLogger<OpenAICompatibleEmbedder>();
        var embedder = new OpenAICompatibleEmbedder(
            new FakeEmbeddingGenerator(inputTokens: 7),
            EmbeddingsProvider.Gemini,
            "gemini-embedding-001",
            1536,
            logger);

        await embedder.EmbedAsync(["only"]);

        logger.Messages.Should().ContainSingle(m =>
            m.Contains("7") && m.Contains("gemini-embedding-001"));
    }

    [Fact]
    public async Task Empty_input_returns_empty_batch_without_calling_provider()
    {
        var generator = new FakeEmbeddingGenerator();
        var embedder = new OpenAICompatibleEmbedder(generator, EmbeddingsProvider.Gemini, "m", 1536, new CapturingLogger<OpenAICompatibleEmbedder>());

        var batch = await embedder.EmbedAsync([]);

        batch.Vectors.Should().BeEmpty();
        batch.InputTokens.Should().Be(0);
        generator.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Exhausted_429_retries_throw_typed_quota_exception()
    {
        var generator = new ThrowingEmbeddingGenerator(status: 429);
        var embedder = new OpenAICompatibleEmbedder(
            generator, EmbeddingsProvider.Gemini, "gemini-embedding-001", 1536,
            new CapturingLogger<OpenAICompatibleEmbedder>(), retryDelay: TimeSpan.Zero);

        var act = () => embedder.EmbedAsync(["x"]);

        await act.Should().ThrowAsync<EmbeddingQuotaExceededException>();
        generator.CallCount.Should().Be(4); // all attempts spent before giving up
    }

    [Fact]
    public async Task Recovers_when_429_clears_within_retry_budget()
    {
        var generator = new ThrowingEmbeddingGenerator(status: 429, failuresBeforeSuccess: 2);
        var embedder = new OpenAICompatibleEmbedder(
            generator, EmbeddingsProvider.Gemini, "gemini-embedding-001", 1536,
            new CapturingLogger<OpenAICompatibleEmbedder>(), retryDelay: TimeSpan.Zero);

        var batch = await embedder.EmbedAsync(["x"]);

        batch.Vectors.Should().HaveCount(1);
        generator.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task Non_429_provider_errors_propagate_unwrapped()
    {
        var generator = new ThrowingEmbeddingGenerator(status: 500);
        var embedder = new OpenAICompatibleEmbedder(
            generator, EmbeddingsProvider.Gemini, "gemini-embedding-001", 1536,
            new CapturingLogger<OpenAICompatibleEmbedder>(), retryDelay: TimeSpan.Zero);

        var act = () => embedder.EmbedAsync(["x"]);

        await act.Should().ThrowAsync<ClientResultException>();
        generator.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Quota_breaker_fails_fast_until_window_elapses()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-08-11T00:00:00Z"));
        var generator = new ThrowingEmbeddingGenerator(status: 429);
        var embedder = new OpenAICompatibleEmbedder(
            generator, EmbeddingsProvider.Gemini, "gemini-embedding-001", 1536, new CapturingLogger<OpenAICompatibleEmbedder>(),
            retryDelay: TimeSpan.Zero, clock: clock, quotaBreakerWindow: TimeSpan.FromMinutes(30));

        await embedder.Invoking(e => e.EmbedAsync(["a"]))
            .Should().ThrowAsync<EmbeddingQuotaExceededException>();
        generator.CallCount.Should().Be(4);

        // Breaker open: fail fast, no provider calls spent.
        await embedder.Invoking(e => e.EmbedAsync(["b"]))
            .Should().ThrowAsync<EmbeddingQuotaExceededException>();
        generator.CallCount.Should().Be(4);

        // Window elapsed: the provider is probed again.
        clock.Advance(TimeSpan.FromMinutes(31));
        await embedder.Invoking(e => e.EmbedAsync(["c"]))
            .Should().ThrowAsync<EmbeddingQuotaExceededException>();
        generator.CallCount.Should().Be(8);
    }

    /// <summary>
    /// The precedence a 429 wait is chosen by (EXP-67, ADR §2 decision 7): <c>retry-after-ms</c>
    /// first, then <c>Retry-After</c>, then the caller's escalating fallback. Asserted on the pure
    /// function rather than by timing a real wait, so what is proven is the choice rather than a
    /// stopwatch reading.
    ///
    /// <para>The row that matters most is the first: both headers present, the millisecond one
    /// winning. Taking seconds where milliseconds were offered would round every 300&#160;ms
    /// throttle up to a full second, on every attempt, and no test that looked at one header at a
    /// time would notice.</para>
    /// </summary>
    [Theory]
    // both present: the precise one wins, and it is not the one that would round up
    [InlineData("300", "1", 0.3)]
    [InlineData(null, "7", 7.0)]
    [InlineData("2500", null, 2.5)]
    // neither: the caller's escalating fallback, untouched
    [InlineData(null, null, 40.0)]
    // nonsense in either header is not a wait anybody chose, and a negative number is nonsense:
    // the fallback is a length somebody picked, where zero would be a busy retry loop
    [InlineData("soon", null, 40.0)]
    [InlineData(null, "soon", 40.0)]
    [InlineData("-5", null, 40.0)]
    [InlineData(null, "-5", 40.0)]
    // and no provider gets to hold a reconcile pass open for an hour
    [InlineData(null, "3600", 60.0)]
    public void Honours_retry_after_ms_then_Retry_After_on_429(
        string? retryAfterMs, string? retryAfter, double expectedSeconds)
    {
        var failure = Throttled(retryAfterMs, retryAfter);

        OpenAICompatibleEmbedder.RetryDelayFor(failure, fallback: TimeSpan.FromSeconds(40))
            .Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    /// <summary>An HTTP-date is the other form <c>Retry-After</c> is allowed to take. A provider
    /// that sends one and gets the fallback instead is waiting a length nobody chose.</summary>
    [Fact]
    public void An_http_date_Retry_After_is_honoured_as_the_time_until_it()
    {
        var failure = Throttled(null, DateTimeOffset.UtcNow.AddSeconds(30).ToString("R"));

        OpenAICompatibleEmbedder.RetryDelayFor(failure, fallback: TimeSpan.FromSeconds(40))
            .Should().BeCloseTo(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2));
    }

    /// <summary>A date already past means "now", not a negative delay that would throw out of
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</summary>
    [Fact]
    public void An_http_date_already_past_means_no_wait()
        => OpenAICompatibleEmbedder
            .RetryDelayFor(
                Throttled(null, DateTimeOffset.UtcNow.AddMinutes(-5).ToString("R")),
                fallback: TimeSpan.FromSeconds(40))
            .Should().Be(TimeSpan.Zero);

    /// <summary>A response with no headers at all — which is what an SDK exception raised before a
    /// response arrived looks like — falls back rather than throwing.</summary>
    [Fact]
    public void A_429_carrying_no_response_falls_back()
        => OpenAICompatibleEmbedder
            .RetryDelayFor(new ThrowingEmbeddingGenerator.FakeClientResultException(429), TimeSpan.FromSeconds(40))
            .Should().Be(TimeSpan.FromSeconds(40));

    /// <summary>And the embedder actually uses it: the wait it announces on a real retry is the
    /// header's, not the fallback's. Read off the log line, which is the only place the chosen
    /// delay is observable without timing one.</summary>
    [Fact]
    public async Task The_retry_loop_waits_for_what_the_provider_asked_for()
    {
        var logger = new CapturingLogger<OpenAICompatibleEmbedder>();
        var embedder = new OpenAICompatibleEmbedder(
            new ThrowingEmbeddingGenerator(status: 429, retryAfterMs: "1"),
            EmbeddingsProvider.AzureFoundry, "text-embedding-3-small", 1536, logger,
            // 20s would be the fallback's first step; the header has to beat it.
            retryDelay: TimeSpan.FromSeconds(20));

        await embedder.Invoking(e => e.EmbedAsync(["x"]))
            .Should().ThrowAsync<EmbeddingQuotaExceededException>();

        logger.Messages.Should().Contain(m => m.Contains("waiting 0.001s"))
            .And.NotContain(m => m.Contains("waiting 20s"));
    }

    /// <summary>The window the breaker opens for is the one it was built with — the active
    /// provider's <c>QuotaBreakerSeconds</c>. Which provider's number reaches a container is
    /// asserted in <c>EmbeddingProviderTests</c>; this is that the embedder honours it.</summary>
    [Fact]
    public void Quota_breaker_opens_for_the_active_providers_seconds()
        => new OpenAICompatibleEmbedder(
                new FakeEmbeddingGenerator(), EmbeddingsProvider.AzureFoundry,
                "text-embedding-3-small", 1536, new CapturingLogger<OpenAICompatibleEmbedder>(),
                quotaBreakerWindow: TimeSpan.FromSeconds(60))
            .QuotaBreakerWindow.Should().Be(TimeSpan.FromSeconds(60));

    /// <summary>A 429 carrying the headers under test, as the SDK surfaces one.</summary>
    private static ClientResultException Throttled(string? retryAfterMs, string? retryAfter)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (retryAfterMs is not null)
        {
            headers["retry-after-ms"] = retryAfterMs;
        }

        if (retryAfter is not null)
        {
            headers["Retry-After"] = retryAfter;
        }

        return new ClientResultException(new ThrottledResponse(headers));
    }

    /// <summary>The minimum <see cref="PipelineResponse"/> a 429 needs to carry headers.</summary>
    private sealed class ThrottledResponse(IReadOnlyDictionary<string, string> headers) : PipelineResponse
    {
        public override int Status => 429;

        public override string ReasonPhrase => "Too Many Requests";

        protected override PipelineResponseHeaders HeadersCore { get; } = new HeaderBag(headers);

        public override Stream? ContentStream { get; set; }

        public override BinaryData Content { get; } = BinaryData.FromString("");

        public override BinaryData BufferContent(CancellationToken ct = default) => Content;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken ct = default)
            => ValueTask.FromResult(Content);

        public override void Dispose() { }

        private sealed class HeaderBag(IReadOnlyDictionary<string, string> values) : PipelineResponseHeaders
        {
            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator()
                => values.GetEnumerator();

            public override bool TryGetValue(string name, out string? value)
                => values.TryGetValue(name, out value);

            public override bool TryGetValues(string name, out IEnumerable<string>? values_)
            {
                if (values.TryGetValue(name, out var single))
                {
                    values_ = [single];
                    return true;
                }

                values_ = null;
                return false;
            }
        }
    }

    private sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>Deterministic offline stand-in for an OpenAI embedding client.</summary>
    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly int _dimensions;
        private readonly long _inputTokens;

        public FakeEmbeddingGenerator(int dimensions = 1536, long inputTokens = 1)
        {
            _dimensions = dimensions;
            _inputTokens = inputTokens;
        }

        public int CallCount { get; private set; }

        public EmbeddingGenerationOptions? LastOptions { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOptions = options;
            var items = values.Select(v => new Embedding<float>(Seed(v, _dimensions)));
            var generated = new GeneratedEmbeddings<Embedding<float>>(items)
            {
                Usage = new UsageDetails { InputTokenCount = _inputTokens },
            };
            return Task.FromResult(generated);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }

        // Stable pseudo-vector from the input text — same text always yields the same vector.
        private static float[] Seed(string text, int dimensions)
        {
            var seed = 17;
            foreach (var c in text)
            {
                seed = unchecked(seed * 31 + c);
            }

            var vector = new float[dimensions];
            for (var i = 0; i < dimensions; i++)
            {
                vector[i] = ((seed + i) % 1000) / 1000f;
            }

            return vector;
        }
    }

    /// <summary>Throws <see cref="ClientResultException"/> with the given status until
    /// <c>failuresBeforeSuccess</c> calls have failed, then delegates to the deterministic fake.</summary>
    private sealed class ThrowingEmbeddingGenerator(
        int status, int? failuresBeforeSuccess = null, string? retryAfterMs = null)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly FakeEmbeddingGenerator _inner = new();

        public int CallCount { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (failuresBeforeSuccess is { } limit && CallCount > limit)
            {
                return _inner.GenerateAsync(values, options, cancellationToken);
            }

            throw retryAfterMs is null
                ? new FakeClientResultException(status)
                : Throttled(retryAfterMs, null);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }

        /// <summary>A provider error with no response behind it — what an SDK raises when the call
        /// failed before one arrived.</summary>
        internal sealed class FakeClientResultException : ClientResultException
        {
            public FakeClientResultException(int status)
                : base($"provider returned {status}")
                => Status = status;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
