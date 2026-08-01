using System.Text.RegularExpressions;

namespace CvManager.Agents.Agents;

/// <summary>
/// Pure, endpoint-side vetting of one model rewrite (P1T-65). Two rules, both conservative —
/// a violation drops the rewrite, with no retry:
/// <list type="number">
/// <item><b>Numbers-subset</b>: every numeric token in the rewrite (<c>40%</c>, <c>10x</c>,
/// <c>4.5</c>, <c>90k</c>, <c>2020</c>, …) must already appear in the original bullet or its
/// parent experience's text (so dates/durations from the experience header are legitimate).
/// A number the CV never stated is a fabrication, whatever its source.</item>
/// <item><b>Exemplar-overlap</b>: no verbatim run of <paramref name="exemplarNGramWords"/>
/// words (default 8, case-insensitive, punctuation-blind) shared with any style exemplar shown
/// this run — exemplars are other people's CVs and may lend phrasing quality, never phrases.</item>
/// </list>
/// </summary>
public static partial class FabricationGuard
{
    /// <summary>Checks one rewrite. Returns null when it passes, or a human-readable violation
    /// description (for the warning log) when it must be dropped.</summary>
    public static string? Check(
        string rewritten,
        string original,
        string experienceContext,
        IEnumerable<string> exemplarTexts,
        int exemplarNGramWords = 8)
    {
        var allowedNumbers = NumericTokens(original + " " + experienceContext);
        var fabricated = NumericTokens(rewritten).Where(t => !allowedNumbers.Contains(t)).ToList();
        if (fabricated.Count > 0)
        {
            return $"numeric token(s) [{string.Join(", ", fabricated)}] not present in the original bullet or its experience context";
        }

        var rewriteNGrams = NGrams(Words(rewritten), exemplarNGramWords).ToHashSet();
        if (rewriteNGrams.Count > 0)
        {
            foreach (var exemplar in exemplarTexts)
            {
                foreach (var nGram in NGrams(Words(exemplar), exemplarNGramWords))
                {
                    if (rewriteNGrams.Contains(nGram))
                    {
                        return $"verbatim {exemplarNGramWords}-word overlap with a style exemplar (\"{nGram}\")";
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Numeric tokens, lowercased: digits with optional decimal/thousands separators and
    /// an optional short alphabetic/percent suffix ("40%", "10x", "4.5", "90k", "2020").</summary>
    private static HashSet<string> NumericTokens(string text)
        => NumericTokenRegex().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToHashSet();

    private static IReadOnlyList<string> Words(string text)
        => WordRegex().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();

    private static IEnumerable<string> NGrams(IReadOnlyList<string> words, int n)
    {
        for (var i = 0; i + n <= words.Count; i++)
        {
            yield return string.Join(' ', words.Skip(i).Take(n));
        }
    }

    [GeneratedRegex(@"\d+(?:[.,]\d+)*(?:[a-z%+]+)?")]
    private static partial Regex NumericTokenRegex();

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordRegex();
}
