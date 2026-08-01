using System.Globalization;

namespace CvManager.RetrievalEval;

/// <summary>
/// Threshold-list generation for the eval sweep. All arithmetic is done on integer multiples of the
/// step (then rounded to 4 decimals) so 0.15 + 7×0.05 lands exactly on 0.50 instead of drifting.
/// Thresholds are cosine similarities, so everything lives in [0, 1].
/// </summary>
public static class SweepRange
{
    private const string Grammar = "start:end:step (e.g. 0.15:0.50:0.05), with 0 <= start <= end <= 1 and step > 0";

    /// <summary>Parse a <c>start:end:step</c> spec into the inclusive threshold list.</summary>
    public static IReadOnlyList<double> Parse(string spec)
    {
        var parts = spec.Split(':');
        if (parts.Length != 3
            || !TryParse(parts[0], out var start)
            || !TryParse(parts[1], out var end)
            || !TryParse(parts[2], out var step))
        {
            throw new ArgumentException($"Sweep spec '{spec}' is not {Grammar}.", nameof(spec));
        }

        if (step <= 0 || start < 0 || end > 1 || end < start)
        {
            throw new ArgumentException($"Sweep spec '{spec}' is out of range; expected {Grammar}.", nameof(spec));
        }

        return Steps(start, end, step);
    }

    /// <summary>The refine window: <paramref name="radius"/> around a winner in exact steps,
    /// clamped to the [0, 1] similarity domain.</summary>
    public static IReadOnlyList<double> Around(double center, double radius, double step)
        => Steps(center - radius, center + radius, step)
            .Where(t => t is >= 0 and <= 1)
            .ToList();

    /// <summary>Inclusive [start, end] in integer step multiples (a hair of slack absorbs binary
    /// floating-point drift without ever stepping past the end).</summary>
    private static List<double> Steps(double start, double end, double step)
    {
        var thresholds = new List<double>();
        for (var i = 0; ; i++)
        {
            var value = start + i * step;
            if (value > end + 1e-9)
            {
                break;
            }

            thresholds.Add(Math.Round(value, 4));
        }

        return thresholds;
    }

    private static bool TryParse(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
