using System.Text.RegularExpressions;

namespace ExpertToJob.Tools.DemoRoster;

/// <summary>
/// Offline narrative writer: picks a hand-authored template from <see cref="NarrativeFragments"/>
/// and fills its {slots} from seeded randomness. Every slot draw is independent, so even two
/// experiences built from the same template come out textually distinct.
/// </summary>
public sealed partial class FragmentNarrativeSource : INarrativeSource
{
    private static readonly string[] Percents =
        ["12", "14", "15", "16", "18", "20", "22", "24", "25", "28", "30", "32", "35", "38", "40", "42", "45", "48", "52", "55", "60", "65", "70"];

    private static readonly string[] Quantities =
        ["6", "8", "9", "11", "12", "14", "16", "18", "21", "24", "25", "28", "30", "35", "40", "45", "50", "60", "75", "90"];

    private static readonly string[] BigQuantities =
        ["15k", "40k", "60k", "90k", "120k", "250k", "400k", "600k", "900k", "1.2M", "2M", "3M", "8M", "20M"];

    private static readonly string[] Latencies =
        ["45 ms", "60 ms", "80 ms", "120 ms", "150 ms", "200 ms", "250 ms", "300 ms", "450 ms", "600 ms"];

    private static readonly string[] Multipliers = ["2x", "3x", "4x", "5x", "7x", "10x"];

    public string WriteExpertSummary(string industry, string title, IReadOnlyList<string> topSkills, DeterministicRandom rng)
    {
        var template = rng.Pick(NarrativeFragments.For(industry).ExpertSummaries);
        return Fill(template, company: null, topSkills, rng);
    }

    public ExperienceNarrative WriteExperience(NarrativeContext context, DeterministicRandom rng)
    {
        var narratives = NarrativeFragments.For(context.Industry);
        var group = context.AcronymHeavy ? narratives.AcronymHeavy : narratives.Standard;

        var summary = Fill(rng.Pick(group.Summaries), context.Company, context.Skills, rng);
        var achievements = rng.Sample(group.Achievements, rng.Next(2, 5))
            .Select(t => Fill(t, context.Company, context.Skills, rng))
            .ToList();

        return new ExperienceNarrative(summary, achievements);
    }

    private static string Fill(string template, string? company, IReadOnlyList<string> skills, DeterministicRandom rng) =>
        SlotPattern().Replace(template, match => match.Groups[1].Value switch
        {
            "company" => company ?? throw new InvalidOperationException($"{{company}} slot without a company: '{template}'"),
            "skill" => rng.Pick(skills),
            "pct" => rng.Pick(Percents),
            "qty" => rng.Pick(Quantities),
            "kqty" => rng.Pick(BigQuantities),
            "ms" => rng.Pick(Latencies),
            "x" => rng.Pick(Multipliers),
            "team" => rng.Next(3, 14).ToString(),
            "months" => rng.Next(3, 18).ToString(),
            "yrs" => rng.Next(3, 12).ToString(),
            var unknown => throw new InvalidOperationException($"Unknown narrative slot '{{{unknown}}}' in template: '{template}'"),
        });

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex SlotPattern();
}
