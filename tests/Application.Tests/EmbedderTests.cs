using System.ClientModel;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ExpertToJob.Application.Tests;

/// <summary>
/// Unit tests for <see cref="GeminiEmbedder"/> using a deterministic fake generator — no
/// network. Verifies the batch shape, the reported token count, and that spend is logged (embedding
/// cost is tracked for visibility, deliberately not charged to per-user caps).
/// </summary>
public class EmbedderTests
{
    [Fact]
    public async Task Embeds_batch_preserving_order_and_reports_tokens()
    {
        var logger = new CapturingLogger<GeminiEmbedder>();
        var generator = new FakeEmbeddingGenerator(dimensions: 1536, inputTokens: 42);
        var embedder = new GeminiEmbedder(generator, "gemini-embedding-001", 1536, logger);

        var batch = await embedder.EmbedAsync(["alpha", "beta"]);

        batch.Vectors.Should().HaveCount(2);
        batch.Vectors[0].Should().HaveCount(1536);
        batch.InputTokens.Should().Be(42);
        embedder.Model.Should().Be("gemini-embedding-001");
        // gemini-embedding-001 defaults to 3072 dims; the vector(1536) column depends on this option.
        generator.LastOptions!.Dimensions.Should().Be(1536);
    }

    [Fact]
    public async Task Logs_token_count_and_model()
    {
        var logger = new CapturingLogger<GeminiEmbedder>();
        var embedder = new GeminiEmbedder(
            new FakeEmbeddingGenerator(inputTokens: 7),
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
        var embedder = new GeminiEmbedder(generator, "m", 1536, new CapturingLogger<GeminiEmbedder>());

        var batch = await embedder.EmbedAsync([]);

        batch.Vectors.Should().BeEmpty();
        batch.InputTokens.Should().Be(0);
        generator.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Exhausted_429_retries_throw_typed_quota_exception()
    {
        var generator = new ThrowingEmbeddingGenerator(status: 429);
        var embedder = new GeminiEmbedder(
            generator, "gemini-embedding-001", 1536,
            new CapturingLogger<GeminiEmbedder>(), retryDelay: TimeSpan.Zero);

        var act = () => embedder.EmbedAsync(["x"]);

        await act.Should().ThrowAsync<EmbeddingQuotaExceededException>();
        generator.CallCount.Should().Be(4); // all attempts spent before giving up
    }

    [Fact]
    public async Task Recovers_when_429_clears_within_retry_budget()
    {
        var generator = new ThrowingEmbeddingGenerator(status: 429, failuresBeforeSuccess: 2);
        var embedder = new GeminiEmbedder(
            generator, "gemini-embedding-001", 1536,
            new CapturingLogger<GeminiEmbedder>(), retryDelay: TimeSpan.Zero);

        var batch = await embedder.EmbedAsync(["x"]);

        batch.Vectors.Should().HaveCount(1);
        generator.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task Non_429_provider_errors_propagate_unwrapped()
    {
        var generator = new ThrowingEmbeddingGenerator(status: 500);
        var embedder = new GeminiEmbedder(
            generator, "gemini-embedding-001", 1536,
            new CapturingLogger<GeminiEmbedder>(), retryDelay: TimeSpan.Zero);

        var act = () => embedder.EmbedAsync(["x"]);

        await act.Should().ThrowAsync<ClientResultException>();
        generator.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Quota_breaker_fails_fast_until_window_elapses()
    {
        var clock = new TestClock(DateTimeOffset.Parse("2026-08-11T00:00:00Z"));
        var generator = new ThrowingEmbeddingGenerator(status: 429);
        var embedder = new GeminiEmbedder(
            generator, "gemini-embedding-001", 1536, new CapturingLogger<GeminiEmbedder>(),
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
    private sealed class ThrowingEmbeddingGenerator(int status, int? failuresBeforeSuccess = null)
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

            throw new FakeClientResultException(status);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }

        private sealed class FakeClientResultException : ClientResultException
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
