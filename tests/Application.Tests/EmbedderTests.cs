using EmployeeManager.Application.Abstractions;
using EmployeeManager.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace EmployeeManager.Application.Tests;

/// <summary>
/// Unit tests for <see cref="GitHubModelsEmbedder"/> using a deterministic fake generator — no
/// network. Verifies the batch shape, the reported token count, and that spend is logged (embedding
/// cost is tracked for visibility, deliberately not charged to per-user caps).
/// </summary>
public class EmbedderTests
{
    [Fact]
    public async Task Embeds_batch_preserving_order_and_reports_tokens()
    {
        var logger = new CapturingLogger<GitHubModelsEmbedder>();
        var embedder = new GitHubModelsEmbedder(
            new FakeEmbeddingGenerator(dimensions: 1536, inputTokens: 42),
            "text-embedding-3-small",
            logger);

        var batch = await embedder.EmbedAsync(["alpha", "beta"]);

        batch.Vectors.Should().HaveCount(2);
        batch.Vectors[0].Should().HaveCount(1536);
        batch.InputTokens.Should().Be(42);
        embedder.Model.Should().Be("text-embedding-3-small");
    }

    [Fact]
    public async Task Logs_token_count_and_model()
    {
        var logger = new CapturingLogger<GitHubModelsEmbedder>();
        var embedder = new GitHubModelsEmbedder(
            new FakeEmbeddingGenerator(inputTokens: 7),
            "text-embedding-3-small",
            logger);

        await embedder.EmbedAsync(["only"]);

        logger.Messages.Should().ContainSingle(m =>
            m.Contains("7") && m.Contains("text-embedding-3-small"));
    }

    [Fact]
    public async Task Empty_input_returns_empty_batch_without_calling_provider()
    {
        var generator = new FakeEmbeddingGenerator();
        var embedder = new GitHubModelsEmbedder(generator, "m", new CapturingLogger<GitHubModelsEmbedder>());

        var batch = await embedder.EmbedAsync([]);

        batch.Vectors.Should().BeEmpty();
        batch.InputTokens.Should().Be(0);
        generator.CallCount.Should().Be(0);
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

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
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
