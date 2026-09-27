using System.Globalization;
using ExpertToJob.Infrastructure.Embeddings;

namespace ExpertToJob.RetrievalEval;

/// <summary>
/// The sweep CLI's parsed arguments:
/// <c>[--provider Gemini|AzureFoundry] [--threshold X | --sweep start:end:step] [--refine]
/// [--output path] [--date d]</c>. The date is a plain string the report echoes verbatim —
/// "unspecified" unless the caller passes one.
///
/// <para><see cref="Thresholds"/> and <see cref="Provider"/> are both <c>null</c> when the caller
/// did not say: since EXP-64 the eval reads the provider from <c>Ai:*</c> configuration and takes
/// its floor from that provider's block, so a code default here would be one provider's number
/// silently applied to another's vectors. The flags are overrides, not defaults.</para>
/// </summary>
public sealed record CliArgs(
    IReadOnlyList<double>? Thresholds,
    bool IsSweep,
    bool Refine,
    string? OutputPath,
    string Date,
    EmbeddingsProvider? Provider)
{
    public static CliArgs Parse(IReadOnlyList<string> argv)
    {
        double? threshold = null;
        IReadOnlyList<double>? sweep = null;
        var refine = false;
        string? output = null;
        var date = "unspecified";
        EmbeddingsProvider? provider = null;

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
                case "--provider":
                    provider = ParseProvider(Value(argv, ref i, "--provider"));
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
            Thresholds: sweep ?? (threshold is { } single ? [single] : null),
            IsSweep: sweep is not null,
            Refine: refine,
            OutputPath: output,
            Date: date,
            Provider: provider);
    }

    /// <summary>The override has to <b>name a member</b>, the same test the seam applies to
    /// <c>Ai:Embeddings:Provider</c> — a bare number or a comma-separated pair is not a provider
    /// anyone meant.</summary>
    private static EmbeddingsProvider ParseProvider(string text)
        => Enum.GetNames<EmbeddingsProvider>()
               .FirstOrDefault(name => string.Equals(name, text.Trim(), StringComparison.OrdinalIgnoreCase))
           is { } named
            ? Enum.Parse<EmbeddingsProvider>(named)
            : throw new ArgumentException(
                $"--provider '{text}' is not an embeddings provider this build knows. "
                + $"Valid values: {string.Join(", ", Enum.GetNames<EmbeddingsProvider>())}.");

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
