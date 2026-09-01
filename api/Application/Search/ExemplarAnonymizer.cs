using System.Text.RegularExpressions;

namespace ExpertToJob.Application.Search;

/// <summary>
/// Scrubs identifying detail out of an exemplar bullet before it leaves the service: every
/// occurrence of the source expert's first/last name becomes <c>[name]</c> and every occurrence
/// of any of their employers' names becomes <c>[company]</c>. Matching is case-insensitive and
/// whole-word (so "Mark" never mangles "benchmark"); multi-word company names collapse to a single
/// placeholder, and longer names win over their own prefixes.
/// </summary>
public static class ExemplarAnonymizer
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public static string Scrub(
        string text, string firstName, string lastName, IReadOnlyCollection<string> companyNames)
    {
        text = Replace(text, companyNames, "[company]");
        text = Replace(text, [firstName, lastName], "[name]");
        return text;
    }

    private static string Replace(string text, IEnumerable<string> terms, string placeholder)
    {
        // One alternation, longest term first, so "Acme Payments" is consumed before "Acme".
        var cleaned = terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(t => t.Length)
            .Select(Regex.Escape)
            .ToList();
        if (cleaned.Count == 0)
        {
            return text;
        }

        // \b anchors only bind next to word characters, so guard both ends manually to stay
        // "whole-word-ish" even for terms starting/ending in punctuation.
        var pattern = $@"(?<!\w)(?:{string.Join("|", cleaned)})(?!\w)";
        return Regex.Replace(text, pattern, placeholder, RegexOptions.IgnoreCase, MatchTimeout);
    }
}
