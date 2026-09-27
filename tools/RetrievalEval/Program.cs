using System.Globalization;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Infrastructure.Embeddings;
using ExpertToJob.Infrastructure.Persistence;
using ExpertToJob.RetrievalEval;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

// Retrieval eval sweep CLI (P1T-52).
//
//   dotnet run -- [--provider Gemini] [--threshold 0.55 | --sweep 0.30:0.80:0.05] [--refine]
//                 [--output path.md] [--date d]
//
// Spins up a disposable pgvector container, seeds the frozen eval corpus, indexes it with the REAL
// embeddings of the configured provider, runs the golden set ONCE at the sweep floor, then scores
// every candidate threshold as a pure in-memory re-rank. Needs Docker and the active provider's key.
//
// Which provider, and which floor, come from Ai:* configuration through the same seam the MCP host
// uses (EXP-64) — environment variables here, since this tool ships no settings file. --provider
// overrides the discriminator; with no --threshold and no --sweep the run uses that provider's own
// MinSimilarity rather than a number compiled into the CLI.

const double RefineRadius = 0.025;
const double RefineStep = 0.005;

CliArgs options;
try
{
    options = CliArgs.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(
        "Usage: dotnet run -- [--provider Gemini] [--threshold 0.55 | --sweep 0.30:0.80:0.05] "
        + "[--refine] [--output path.md] [--date yyyy-MM-dd]");
    return 2;
}

// The provider, its endpoint, its model and its similarity floor, all from one read of the seam.
var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddInMemoryCollection(options.Provider is { } chosen
        ?
        [
            new KeyValuePair<string, string?>(
                EmbeddingServiceCollectionExtensions.ProviderKey, chosen.ToString()),
        ]
        : [])
    .Build();

EmbeddingsProvider provider;
EmbeddingOptions embeddingOptions;
try
{
    (provider, embeddingOptions) = EmbeddingServiceCollectionExtensions.ResolveProvider(config);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var apiKeyVariable = EmbeddingOptions.ApiKeyVariableFor(provider);
if (string.IsNullOrWhiteSpace(
        EmbeddingServiceCollectionExtensions.ResolveApiKey(
            provider, embeddingOptions, Environment.GetEnvironmentVariable)))
{
    Console.Error.WriteLine(
        $"{apiKeyVariable} is not set. The eval embeds with the real {provider} backend and cannot " +
        $"run without a key. Export one and retry: {apiKeyVariable}=<key> dotnet run -- ...");
    return 1;
}

var corpus = EvalFixtures.LoadCorpus();
var goldenSet = EvalFixtures.LoadGoldenSet();

// No --threshold and no --sweep means "the floor this provider actually runs on", read from its
// own configuration block rather than compiled in: the two providers' plateaus do not overlap.
var thresholds = options.Thresholds ?? [embeddingOptions.MinSimilarity];

// Capture low enough that a later --refine dip below the coarse winner stays inside the capture.
var floor = Math.Max(0, thresholds.Min() - (options.Refine ? RefineRadius : 0));

Console.Error.WriteLine("Starting pgvector container...");
await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
    .Build();
await postgres.StartAsync();

AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.UseVector())
    .Options);

await using (var db = NewDb())
{
    await db.Database.MigrateAsync();
}

// The same real embedding registration production uses (AddEmbeddingProvider + the provider's key).
await using var services = new ServiceCollection()
    .AddLogging()
    .AddEmbeddingProvider(config)
    .BuildServiceProvider();
var embedder = services.GetRequiredService<IEmbedder>();

Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"Seeding {corpus.Count} experts, indexing, and running {goldenSet.Count} queries " +
    $"once at floor {floor:F3} (provider: {provider}, model: {embedder.Model})..."));
var cached = await EvalRunner.CaptureAsync(
    NewDb, embedder, corpus, goldenSet, floor, QueryRetryPolicy.Default);

var results = SweepEvaluator.Sweep(cached, thresholds).ToList();

double? selected = null;
if (options.Refine)
{
    var coarseWinner = ThresholdSelector.SelectWinner(results);
    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"Coarse winner: {coarseWinner.Threshold:F3}; refining ±{RefineRadius} in {RefineStep} steps..."));

    var refineThresholds = SweepRange
        .Around(coarseWinner.Threshold, RefineRadius, RefineStep)
        .Where(t => !results.Any(r => Math.Abs(r.Threshold - t) < 1e-9));
    results = results
        .Concat(SweepEvaluator.Sweep(cached, refineThresholds))
        .OrderBy(r => r.Threshold)
        .ToList();
    selected = ThresholdSelector.SelectWinner(results).Threshold;
}
else if (options.IsSweep)
{
    selected = ThresholdSelector.SelectWinner(results).Threshold;
}

var report = MarkdownReport.Render(
    new ReportMetadata(embedder.Model, corpus.Count, goldenSet.Count, options.Date),
    results,
    selected);

if (options.OutputPath is { } path)
{
    await File.WriteAllTextAsync(path, report);
    Console.Error.WriteLine($"Report written to {path}");
}
else
{
    Console.WriteLine(report);
}

return 0;
