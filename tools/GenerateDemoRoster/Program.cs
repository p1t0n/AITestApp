using CvManager.Infrastructure.Persistence.SeedData;

namespace CvManager.Tools.DemoRoster;

/// <summary>
/// One-off repo tool (P1T-48): generates api/Infrastructure/Persistence/SeedData/demo-roster.json.
/// Assembly is fully deterministic (seeded); narrative prose optionally gets an LLM polish pass
/// via Gemini when GEMINI_API_KEY is set. See README.md next to this file.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = new GenerationOptions();
        string? output = null;
        var offline = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--count": options = options with { EmployeeCount = int.Parse(args[++i]) }; break;
                case "--seed": options = options with { Seed = int.Parse(args[++i]) }; break;
                case "--output": output = args[++i]; break;
                case "--offline": offline = true; break;
                default:
                    Console.Error.WriteLine($"Unknown argument '{args[i]}'. Usage: [--count N] [--seed N] [--output path] [--offline]");
                    return 1;
            }
        }

        output ??= Path.Combine(FindRepoRoot(), "api", "Infrastructure", "Persistence", "SeedData", "demo-roster.json");

        var dataset = DemoRosterGenerator.Generate(options, new FragmentNarrativeSource());
        Console.WriteLine($"Assembled {dataset.Employees.Count} employees / {dataset.Skills.Count} catalog skills (seed {options.Seed}).");

        var token = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (offline || string.IsNullOrWhiteSpace(token))
        {
            Console.WriteLine("Offline mode: keeping fragment-assembled narratives (set GEMINI_API_KEY for the LLM polish pass).");
        }
        else
        {
            Console.WriteLine("Rewriting narratives via Gemini (best-effort, batched)...");
            var enriched = await new GeminiEnricher(token).EnrichAsync(dataset, Console.WriteLine);
            Console.WriteLine($"LLM-enriched {enriched}/{dataset.Employees.Count} employees; the rest keep fragment prose.");
        }

        await File.WriteAllTextAsync(output, DemoRosterLoader.Serialize(dataset) + Environment.NewLine);
        Console.WriteLine($"Wrote {output}");
        return 0;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CvManager.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root (CvManager.slnx).");
    }
}
