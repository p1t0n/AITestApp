using System.Text.RegularExpressions;

namespace ExpertToJob.Application.Search;

/// <summary>
/// The quality gate deciding whether an achievement bullet is a usable style exemplar: it must be
/// quantified (contain a number, a percent, or an Nx multiplier — the hallmark of strong CV
/// phrasing) and sit inside a sane length band (too short carries no style, too long is a
/// paragraph, not a bullet).
/// </summary>
public static class ExemplarQualityFilter
{
    // A digit anywhere (covers "250 ms", "42%", "3x"), or a percent/multiplier written with one.
    private static readonly Regex Quantified = new(@"\d|%", RegexOptions.Compiled);

    public static bool Passes(string text, int minChars, int maxChars)
        => text.Length >= minChars && text.Length <= maxChars && Quantified.IsMatch(text);
}
