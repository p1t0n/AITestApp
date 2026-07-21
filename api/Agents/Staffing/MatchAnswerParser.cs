using System.Text.RegularExpressions;

namespace EmployeeManager.Agents.Staffing;

/// <summary>The deterministic facts lifted out of one Match answer: the overall score (0-100) and
/// the band, either null when the answer didn't state them readably.</summary>
public sealed record MatchAnswerFacts(int? Score, string? Band);

/// <summary>
/// Pure parser for the Match agent's markdown answer. The agent's instructions make it emit an
/// "overall score out of 100" and a band (Strong / Moderate / Weak / Insufficient evidence), but
/// the surrounding markdown varies run to run, so this parser is deliberately lenient: it scans
/// only lines that talk about the overall score / band (never gap-analysis prose), and returns
/// nulls — never throws — when nothing readable is found. The raw markdown ships regardless; these
/// facts just make the report sortable/renderable.
/// </summary>
public static partial class MatchAnswerParser
{
    private static readonly string[] Bands = ["Insufficient evidence", "Strong", "Moderate", "Weak"];

    public static MatchAnswerFacts Parse(string answer)
    {
        int? score = null;
        string? band = null;

        foreach (var line in answer.Split('\n'))
        {
            var relevant = line.Contains("overall", StringComparison.OrdinalIgnoreCase)
                || line.Contains("band", StringComparison.OrdinalIgnoreCase);
            if (!relevant)
            {
                continue;
            }

            score ??= ParseScore(line);
            band ??= ParseBand(line);
        }

        return new MatchAnswerFacts(score, band);
    }

    private static int? ParseScore(string line)
    {
        if (!line.Contains("score", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Drop the "/ 100" and "out of 100" denominators first, so the remaining last number on
        // the line is the score itself ("Overall score (out of 100): 91" -> "...: 91").
        var cleaned = DenominatorPattern().Replace(line, "");

        var candidates = NumberPattern().Matches(cleaned);
        if (candidates.Count == 0)
        {
            return null;
        }

        var value = int.Parse(candidates[^1].Value);
        return value is >= 0 and <= 100 ? value : null;
    }

    private static string? ParseBand(string line) =>
        Bands.FirstOrDefault(b => line.Contains(b, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"(/\s*100\b|\bout of 100\b)", RegexOptions.IgnoreCase)]
    private static partial Regex DenominatorPattern();

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberPattern();
}
