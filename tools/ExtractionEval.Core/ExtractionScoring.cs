using ExpertToJob.Agents.Agents;

namespace ExpertToJob.ExtractionEval;

/// <summary>One JD's score against its labels. <see cref="Fabrications"/> is the hard-gate list:
/// invented values on silent slots (seniority/location/minYears) and MustHave requirements with
/// no basis in the JD's labeled concepts. Priority over-claims (a real concept marked MustHave
/// that the JD only mentions) are a precision miss, not a fabrication.</summary>
public sealed record JdScore(
    string Id,
    int RequirementCount,
    double ConceptRecall,
    double MustHavePrecision,
    double EvidenceVerbatimRate,
    bool SeniorityCorrect,
    bool LocationCorrect,
    IReadOnlyList<string> Fabrications,
    string? Fault);

/// <summary>Aggregates over the whole golden set — the numbers the floors gate on.</summary>
public sealed record EvalAggregate(
    IReadOnlyList<JdScore> Scores,
    double ConceptRecall,
    double MustHavePrecision,
    double EvidenceVerbatimRate,
    double SeniorityAccuracy,
    double LocationAccuracy,
    int FabricationCount,
    int FaultCount)
{
    public static EvalAggregate From(IReadOnlyList<JdScore> scores) => new(
        scores,
        scores.Where(s => s.Fault is null).Select(s => s.ConceptRecall).DefaultIfEmpty(0).Average(),
        scores.Where(s => s.Fault is null).Select(s => s.MustHavePrecision).DefaultIfEmpty(0).Average(),
        scores.Where(s => s.Fault is null).Select(s => s.EvidenceVerbatimRate).DefaultIfEmpty(0).Average(),
        scores.Where(s => s.Fault is null).Select(s => s.SeniorityCorrect ? 1.0 : 0).DefaultIfEmpty(0).Average(),
        scores.Where(s => s.Fault is null).Select(s => s.LocationCorrect ? 1.0 : 0).DefaultIfEmpty(0).Average(),
        scores.Sum(s => s.Fabrications.Count),
        scores.Count(s => s.Fault is not null));
}

/// <summary>Pure scoring of one extraction against its labels — deterministic, unit-tested;
/// the live runner and the CLI both call this.</summary>
public static class ExtractionScoring
{
    public static JdScore Score(GoldenJd golden, JdExtractionOutcome outcome)
    {
        if (outcome.Requirements is null)
        {
            return new JdScore(golden.Id, 0, 0, 0, 0, false, false, [],
                outcome.FaultDetail ?? "extraction fault");
        }

        var extraction = outcome.Requirements;
        var requirements = extraction.Requirements;
        var texts = requirements.Select(r => r.Text.ToLowerInvariant()).ToList();
        var fabrications = new List<string>();

        // Recall: a labeled concept group is covered when any produced requirement mentions
        // one of its keywords.
        var covered = golden.ExpectedConcepts.Count(group =>
            group.Any(keyword => texts.Any(t => t.Contains(keyword))));
        var recall = golden.ExpectedConcepts.Length == 0
            ? 1.0
            : (double)covered / golden.ExpectedConcepts.Length;

        // MustHave discipline: a produced MustHave must match a labeled must-have concept.
        // Matching some labeled concept but not a must-have one = priority over-claim (precision
        // miss); matching nothing labeled = fabricated requirement (hard gate).
        var mustHaves = requirements.Where(r => r.Priority == RequirementPriority.MustHave).ToList();
        var justified = 0;
        foreach (var req in mustHaves)
        {
            var text = req.Text.ToLowerInvariant();
            if (golden.MustHaveConcepts.Any(group => group.Any(text.Contains)))
            {
                justified++;
            }
            else if (!golden.ExpectedConcepts.Any(group => group.Any(text.Contains)))
            {
                fabrications.Add($"MustHave requirement with no basis in the JD: '{req.Text}'");
            }
        }

        var precision = mustHaves.Count == 0 ? 1.0 : (double)justified / mustHaves.Count;

        // Honesty slots: silence must round-trip as Unspecified/null — anything else is invented.
        bool seniorityCorrect;
        if (golden.StatedSeniority is { } statedSeniority)
        {
            seniorityCorrect = extraction.Seniority == statedSeniority;
        }
        else
        {
            seniorityCorrect = extraction.Seniority == JdSeniority.Unspecified;
            if (!seniorityCorrect)
            {
                fabrications.Add($"Seniority '{extraction.Seniority}' invented — the JD states none.");
            }
        }

        bool locationCorrect;
        if (golden.StatedLocation is { } statedLocation)
        {
            locationCorrect = extraction.Location?.Contains(statedLocation, StringComparison.OrdinalIgnoreCase) == true;
        }
        else
        {
            locationCorrect = extraction.Location is null;
            if (!locationCorrect)
            {
                fabrications.Add($"Location '{extraction.Location}' invented — the JD states none.");
            }
        }

        if (!golden.YearsStated)
        {
            fabrications.AddRange(requirements
                .Where(r => r.MinYears is not null)
                .Select(r => $"minYears {r.MinYears} invented on '{r.Text}' — the JD states no years."));
        }

        // Vacuously verbatim when the model honestly produced nothing (sparse JDs): zero
        // requirements means zero unverified claims.
        var verbatimRate = requirements.Count == 0
            ? 1.0
            : (double)requirements.Count(r => !r.Inferred) / requirements.Count;

        return new JdScore(
            golden.Id, requirements.Count, recall, precision, verbatimRate,
            seniorityCorrect, locationCorrect, fabrications, Fault: null);
    }
}
