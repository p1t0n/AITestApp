using ExpertToJob.Domain.Entities;

namespace ExpertToJob.Agents.Agents;

/// <summary>One expert's slice of the roster stats, as captured from the MCP
/// <c>expert_list</c> result. Only the fields the aggregates need.</summary>
public sealed record BenchExpert(string Title, string? Location, int CurrentCapacityPercent);

/// <summary>A name/count pair for distributions (titles, locations, frequent candidates).</summary>
public sealed record NameCount(string Name, int Count);

/// <summary>Aggregates over the staffing proposals ledger (P1T-100) — the demand signal: what
/// roles were asked for, how decisions fell, and who keeps getting shortlisted.</summary>
public sealed record ProposalStats(
    int Total,
    int Pending,
    int Approved,
    int Rejected,
    IReadOnlyList<string> RecentJobDescriptions,
    IReadOnlyList<NameCount> FrequentCandidates);

/// <summary>The deterministic aggregates the bench report is grounded in. Every number the model
/// is allowed to mention lives here; the UI renders these directly, no model text involved.</summary>
public sealed record BenchStats(
    int ActiveExperts,
    int FullyAvailable,
    int PartiallyAvailable,
    int FullyBooked,
    double AverageCapacityPercent,
    IReadOnlyList<NameCount> TopTitles,
    IReadOnlyList<NameCount> Locations,
    ProposalStats? Proposals);

/// <summary>The pinned POST /agents/bench-report response: the narrative markdown (or the
/// deterministic fallback summary when the model degraded), the stats it was grounded in, and
/// the degrade notes.</summary>
public sealed record BenchReportResponse(
    string Answer,
    BenchStats Stats,
    IReadOnlyList<string> Notes);

/// <summary>
/// Pure aggregation for the bench report — deterministic facts only, unit-tested directly. The
/// model never computes these; it receives them and writes prose around them.
/// </summary>
public static class BenchStatsComposer
{
    private const int TopN = 8;
    private const int RecentJds = 5;
    private const int JdSnippetChars = 160;

    public static BenchStats Compose(
        IReadOnlyList<BenchExpert>? experts, IReadOnlyList<StaffingProposal>? proposals)
    {
        var roster = experts ?? [];
        return new BenchStats(
            roster.Count,
            roster.Count(e => e.CurrentCapacityPercent >= 100),
            roster.Count(e => e.CurrentCapacityPercent is > 0 and < 100),
            roster.Count(e => e.CurrentCapacityPercent <= 0),
            roster.Count == 0 ? 0 : Math.Round(roster.Average(e => e.CurrentCapacityPercent), 1),
            TopCounts(roster.Select(e => e.Title)),
            TopCounts(roster.Select(e => e.Location).Where(l => !string.IsNullOrWhiteSpace(l))!),
            proposals is null ? null : ComposeProposals(proposals));
    }

    /// <summary>The deterministic fallback narrative when the model call degrades: plain facts
    /// straight from the stats, so the report still says something true.</summary>
    public static string FallbackAnswer(BenchStats stats)
    {
        var lines = new List<string>
        {
            "## Bench report (deterministic summary)",
            "",
            $"- Active experts: {stats.ActiveExperts}",
            $"- Fully available: {stats.FullyAvailable}, partially: {stats.PartiallyAvailable}, fully booked: {stats.FullyBooked}",
            $"- Average available capacity: {stats.AverageCapacityPercent}%",
        };
        if (stats.Proposals is { } p)
        {
            lines.Add($"- Staffing proposals: {p.Total} total ({p.Pending} pending, {p.Approved} approved, {p.Rejected} rejected)");
        }

        return string.Join('\n', lines);
    }

    private static ProposalStats ComposeProposals(IReadOnlyList<StaffingProposal> proposals)
    {
        var recent = proposals
            .OrderByDescending(p => p.CreatedAt)
            .Take(RecentJds)
            .Select(p => p.JobDescription.Length <= JdSnippetChars
                ? p.JobDescription
                : p.JobDescription[..JdSnippetChars] + "…")
            .ToList();

        var frequent = TopCounts(proposals.SelectMany(p => p.Candidates).Select(c => c.Name));

        return new ProposalStats(
            proposals.Count,
            proposals.Count(p => p.Status == StaffingProposalStatus.Pending),
            proposals.Count(p => p.Status == StaffingProposalStatus.Approved),
            proposals.Count(p => p.Status == StaffingProposalStatus.Rejected),
            recent,
            frequent);
    }

    private static List<NameCount> TopCounts(IEnumerable<string> values) => values
        .GroupBy(v => v.Trim(), StringComparer.OrdinalIgnoreCase)
        .Select(g => new NameCount(g.Key, g.Count()))
        .OrderByDescending(x => x.Count)
        .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
        .Take(TopN)
        .ToList();
}
