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
//   dotnet run -- [--threshold 0.55 | --sweep 0.30:0.80:0.05] [--refine] [--output path.md] [--date d]
//
// Spins up a disposable pgvector container, seeds the frozen eval corpus, indexes it with REAL
// Gemini embeddings, runs the golden set ONCE at the sweep floor, then scores every
// candidate threshold as a pure in-memory re-rank. Needs Docker and GEMINI_API_KEY.

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
        "Usage: dotnet run -- [--threshold 0.55 | --sweep 0.30:0.80:0.05] [--refine] [--output path.md] [--date yyyy-MM-dd]");
    return 2;
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")))
{
    Console.Error.WriteLine(
        "GEMINI_API_KEY is not set. The eval embeds with the real Gemini backend and cannot " +
        "run without a key. Export one and retry: GEMINI_API_KEY=<pat> dotnet run -- ...");
    return 1;
}

var corpus = EvalFixtures.LoadCorpus();
var goldenSet = EvalFixtures.LoadGoldenSet();

// Capture low enough that a later --refine dip below the coarse winner stays inside the capture.
var floor = Math.Max(0, options.Thresholds.Min() - (options.Refine ? RefineRadius : 0));

Console.Error.WriteLine("Starting pgvector container...");
await using var postgres = new PostgreSqlBuilder()
    .WithImage("pgvector/pgvector:pg17")
    .Build();
await postgres.StartAsync();

AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(postgres.GetConnectionString(), npgsql => npgsql.UseVector())
    .Options);

await using (var db = NewDb())
{
    await db.Database.MigrateAsync();
}

// The same real embedding registration production uses (AddGeminiEmbeddings + GEMINI_API_KEY).
var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Gemini:Endpoint"] = "https://generativelanguage.googleapis.com/v1beta/openai",
        ["Gemini:EmbeddingModel"] = "gemini-embedding-001",
    })
    .Build();
await using var provider = new ServiceCollection()
    .AddLogging()
    .AddGeminiEmbeddings(config)
    .BuildServiceProvider();
var embedder = provider.GetRequiredService<IEmbedder>();

Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture,
    $"Seeding {corpus.Count} employees, indexing, and running {goldenSet.Count} queries " +
    $"once at floor {floor:F3} (model: {embedder.Model})..."));
var cached = await EvalRunner.CaptureAsync(
    NewDb, embedder, corpus, goldenSet, floor, QueryRetryPolicy.Default);

var results = SweepEvaluator.Sweep(cached, options.Thresholds).ToList();

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
