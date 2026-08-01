using System.Globalization;

namespace CvManager.RetrievalEval;

/// <summary>
/// The sweep CLI's parsed arguments:
/// <c>[--threshold X | --sweep start:end:step] [--refine] [--output path] [--date d]</c>.
/// Defaults to a single run at the production threshold (0.55). The date is a plain string the
/// report echoes verbatim — "unspecified" unless the caller passes one.
/// </summary>
public sealed record CliArgs(
    IReadOnlyList<double> Thresholds,
    bool IsSweep,
    bool Refine,
    string? OutputPath,
    string Date)
{
    public const double DefaultThreshold = 0.55;

    public static CliArgs Parse(IReadOnlyList<string> argv)
    {
        double? threshold = null;
        IReadOnlyList<double>? sweep = null;
        var refine = false;
        string? output = null;
        var date = "unspecified";

        for (var i = 0; i < argv.Count; i++)
        {
            switch (argv[i])
            {
                case "--threshold":
                    threshold = ParseThreshold(Value(argv, ref i, "--threshold"));
                    break;
                case "--sweep":
                    sweep = SweepRange.Parse(Value(argv, ref i, "--sweep"));
                    break;
                case "--refine":
                    refine = true;
                    break;
                case "--output":
                    output = Value(argv, ref i, "--output");
                    break;
                case "--date":
                    date = Value(argv, ref i, "--date");
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{argv[i]}'.");
            }
        }

        if (threshold is not null && sweep is not null)
        {
            throw new ArgumentException("--threshold and --sweep are mutually exclusive.");
        }

        return new CliArgs(
            Thresholds: sweep ?? [threshold ?? DefaultThreshold],
            IsSweep: sweep is not null,
            Refine: refine,
            OutputPath: output,
            Date: date);
    }

    private static string Value(IReadOnlyList<string> argv, ref int i, string flag)
        => ++i < argv.Count
            ? argv[i]
            : throw new ArgumentException($"{flag} requires a value.");

    private static double ParseThreshold(string text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
           && value is >= 0 and <= 1
            ? value
            : throw new ArgumentException($"--threshold '{text}' is not a similarity in [0, 1].");
}
