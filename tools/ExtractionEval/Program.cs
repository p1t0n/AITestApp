using System.ClientModel;
using System.ClientModel.Primitives;
using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Configuration;
using ExpertToJob.ExtractionEval;
using Microsoft.Extensions.AI;
using OpenAI;

// Extraction-fidelity eval CLI (P1T-119).
//
//   GEMINI_API_KEY=<key> dotnet run --project tools/ExtractionEval -- [--output path.md] [--delay 5]
//
// Runs the real JdRequirementExtractor (production prompt + native json_schema on the pinned
// model) over the frozen golden JD set and reports recall / must-have precision / evidence
// verbatim rate / honesty-slot accuracy, with the hard fabrication gate. Exit code 0 = all
// gates green, 1 = a gate violated, 2 = bad usage.

string? outputPath = null;
var delaySeconds = 5.0;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--output" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        case "--delay" when i + 1 < args.Length && double.TryParse(args[i + 1], out var parsed):
            delaySeconds = parsed;
            i++;
            break;
        default:
            Console.Error.WriteLine("Usage: dotnet run -- [--output path.md] [--delay seconds]");
            return 2;
    }
}

var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("GEMINI_API_KEY is not set. Export one and retry.");
    return 1;
}

// The production wiring (endpoint, compat handler, thought-signature policy, pinned model).
var cfg = new GeminiOptions();
var options = new OpenAIClientOptions
{
    Endpoint = new Uri(cfg.Endpoint),
    Transport = new HttpClientPipelineTransport(
        new HttpClient(new GeminiCompatHandler(new HttpClientHandler()))),
};
options.AddPolicy(new GeminiThoughtSignaturePolicy(), PipelinePosition.PerCall);
var chat = new OpenAIClient(new ApiKeyCredential(apiKey), options)
    .GetChatClient(cfg.Model)
    .AsIChatClient();

var goldenSet = GoldenJdSet.Load();
Console.Error.WriteLine($"Running {goldenSet.Count} golden JDs on {cfg.Model} (delay {delaySeconds}s)...");

var aggregate = await ExtractionEvalRunner.RunAsync(
    new JdRequirementExtractor(chat),
    goldenSet,
    TimeSpan.FromSeconds(delaySeconds),
    Console.Error.WriteLine);

var report = ExtractionEvalRunner.RenderReport(
    aggregate, cfg.Model, DateOnly.FromDateTime(DateTime.UtcNow));

if (outputPath is not null)
{
    await File.WriteAllTextAsync(outputPath, report);
    Console.Error.WriteLine($"Report written to {outputPath}");
}
else
{
    Console.WriteLine(report);
}

var violations = ExtractionEvalRunner.GateViolations(aggregate);
if (violations.Count > 0)
{
    Console.Error.WriteLine("GATE VIOLATIONS:");
    foreach (var violation in violations)
    {
        Console.Error.WriteLine($"  - {violation}");
    }

    return 1;
}

Console.Error.WriteLine("All gates green.");
return 0;
