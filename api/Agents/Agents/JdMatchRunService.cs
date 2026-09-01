using System.Text.Json.Serialization;
using ExpertToJob.Agents.Staffing;

namespace ExpertToJob.Agents.Agents;

/// <summary>One candidate's JD-only match result. Identity and the retrieval score are
/// deterministic (from the shortlist tool result); score/band are parsed from the match answer
/// (null when unreadable — the markdown ships regardless); <see cref="Error"/> is set only when
/// that candidate's match run failed (the entry degrades, the call does not).</summary>
public sealed record JdMatchCandidateResult(
    Guid EmployeeId,
    string Name,
    string Title,
    double RetrievalScore,
    string Status,
    int? Score,
    string? Band,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Answer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error);

/// <summary>One metered reply from a JD-only match run, tagged with its pipeline step so the
/// endpoint can record usage per sub-step (same convention as staffing).</summary>
public sealed record JdMatchMeteredReply(string AgentName, AgentReply Reply, string Step);

/// <summary>
/// What one JD-only match run produced. <see cref="FaultDetail"/> is non-null only when the
/// shortlist step failed (nothing to match — the endpoint maps it to a 502); individual match
/// failures degrade into their candidate's entry instead. <see cref="Metered"/> always carries
/// every reply that spent tokens, fault or not.
/// </summary>
public sealed record JdMatchOutcome(
    IReadOnlyList<string> Requirements,
    IReadOnlyList<JdMatchCandidateResult> Results,
    IReadOnlyList<JdMatchMeteredReply> Metered,
    string? FaultDetail);

/// <summary>
/// The core of a JD-only match run (P1T-103, decision #9 "B" of the RAG plan): shortlist
/// retrieval picks the top candidates for the job description, then the match run fans out per
/// candidate under the process-wide staffing throttle — a lighter sibling of the staffing
/// pipeline with no narrative step and no proposal. No HTTP types, no cap-check, no metering:
/// those stay with the endpoint shell.
/// </summary>
public sealed class JdMatchRunService(
    IShortlistRunService shortlist,
    IMatchRunService match,
    StaffingThrottle throttle)
{
    /// <summary>How many retrieved candidates get a match run; mirrors the staffing clamp.</summary>
    public const int MaxTop = 5;

    public async Task<JdMatchOutcome> RunAsync(string jobDescription, int? topK, CancellationToken ct = default)
    {
        var top = Math.Clamp(topK ?? 3, 1, MaxTop);

        var shortlistRun = await shortlist.RunAsync(
            new ShortlistAgentRequest(jobDescription, TopK: top), ct);
        var metered = new List<JdMatchMeteredReply>();
        if (shortlistRun.ExtractionReply is { } extractionReply)
        {
            metered.Add(new(JdRequirementExtractor.AgentName, extractionReply, "jd-extraction"));
        }

        metered.Add(new(shortlistRun.AgentName, shortlistRun.Reply, "jd-shortlist"));

        if (shortlistRun.Response is null)
        {
            return new JdMatchOutcome([], [], metered, shortlistRun.FaultDetail);
        }

        var candidates = shortlistRun.Response.Candidates.Take(top).ToList();
        var results = new JdMatchCandidateResult[candidates.Count];
        var meterGate = new object();

        await Task.WhenAll(candidates.Select(async (candidate, index) =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var run = await match.RunAsync(
                    candidate.EmployeeId, jobDescription, shortlistRun.Response.Extraction, ct);
                lock (meterGate)
                {
                    metered.Add(new JdMatchMeteredReply(run.AgentName, run.Reply, "jd-match"));
                }

                results[index] = new JdMatchCandidateResult(
                    candidate.EmployeeId, candidate.Name, candidate.Title, candidate.Score,
                    StaffingMatchStatus.Completed, run.Score, run.Band, run.Answer, Error: null);
            }
            catch (HttpRequestException ex)
            {
                // One candidate's model/MCP fault degrades that entry; the others still ship.
                results[index] = new JdMatchCandidateResult(
                    candidate.EmployeeId, candidate.Name, candidate.Title, candidate.Score,
                    StaffingMatchStatus.Failed, Score: null, Band: null, Answer: null, Error: ex.Message);
            }
            finally
            {
                throttle.Release();
            }
        }));

        // Retrieval order in, score order out: completed entries sort by match score (desc,
        // unreadable scores last), failed entries trail in retrieval order.
        var ranked = results
            .OrderBy(r => r.Status == StaffingMatchStatus.Failed ? 1 : 0)
            .ThenByDescending(r => r.Score ?? -1)
            .ThenByDescending(r => r.RetrievalScore)
            .ToList();

        return new JdMatchOutcome(shortlistRun.Response.Requirements, ranked, metered, FaultDetail: null);
    }
}
