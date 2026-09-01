using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Staffing;
using ExpertToJob.Agents.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// Unit tests for the JD-only match run (P1T-103): shortlist retrieval picks the candidates,
/// the match run fans out per candidate under the throttle, entries rank by parsed match score,
/// and faults degrade per-entry (match) or as run data (shortlist) — never as exceptions.
/// </summary>
public class JdMatchRunServiceTests
{
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Grace = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ShortlistCandidateItem Candidate(Guid id, string name, double score) => new(
        id, name, "Engineer", score, new ShortlistCoverage(2, 2), [], "rationale");

    private static ShortlistRunOutcome ShortlistOk(params ShortlistCandidateItem[] candidates) => new(
        "shortlist",
        new AgentReply("[]", 100, 20, 120),
        new ShortlistResponse(["kafka", "k8s"], candidates),
        FaultDetail: null);

    private static MatchRunOutcome MatchOutcome(Guid id, int score, string band) => new(
        "match",
        $"Analysis for {id}.",
        new AgentReply("answer", 200, 50, 250),
        score,
        band);

    private static JdMatchRunService Service(
        IShortlistRunService shortlist, IMatchRunService match) =>
        new(shortlist, match, new StaffingThrottle(2));

    [Fact]
    public async Task Fans_out_per_candidate_and_ranks_by_parsed_match_score()
    {
        var shortlist = new FakeShortlistRunService(ShortlistOk(
            Candidate(Ada, "Ada", 0.95), Candidate(Grace, "Grace", 0.80)));
        var match = new FakeMatchRunService((id, _) => Task.FromResult(
            MatchOutcome(id, id == Grace ? 85 : 60, id == Grace ? "Strong" : "Moderate")));

        var outcome = await Service(shortlist, match).RunAsync("JD text", topK: 2);

        outcome.FaultDetail.Should().BeNull();
        outcome.Requirements.Should().Equal("kafka", "k8s");
        match.Calls.Should().Be(2);
        // Grace's match score beats Ada's despite a lower retrieval score.
        outcome.Results.Select(r => r.ExpertId).Should().Equal(Grace, Ada);
        outcome.Results[0].Score.Should().Be(85);
        outcome.Results[0].Band.Should().Be("Strong");
        outcome.Results[0].Status.Should().Be(StaffingMatchStatus.Completed);
        outcome.Results[0].Answer.Should().Contain("Analysis for");
        // Every reply that spent tokens is handed back for metering, step-tagged.
        outcome.Metered.Select(m => m.Step).Should().Equal("jd-shortlist", "jd-match", "jd-match");
    }

    [Fact]
    public async Task A_failed_match_degrades_that_entry_and_trails_the_ranking()
    {
        var shortlist = new FakeShortlistRunService(ShortlistOk(
            Candidate(Ada, "Ada", 0.95), Candidate(Grace, "Grace", 0.80)));
        var match = new FakeMatchRunService((id, _) => id == Ada
            ? Task.FromException<MatchRunOutcome>(new HttpRequestException("model down"))
            : Task.FromResult(MatchOutcome(id, 70, "Moderate")));

        var outcome = await Service(shortlist, match).RunAsync("JD", topK: 2);

        outcome.Results.Should().HaveCount(2);
        outcome.Results[0].ExpertId.Should().Be(Grace);
        var failed = outcome.Results[1];
        failed.ExpertId.Should().Be(Ada);
        failed.Status.Should().Be(StaffingMatchStatus.Failed);
        failed.Error.Should().Be("model down");
        failed.Answer.Should().BeNull();
        // Only the successful match metered a jd-match reply.
        outcome.Metered.Count(m => m.Step == "jd-match").Should().Be(1);
    }

    [Fact]
    public async Task A_shortlist_fault_surfaces_as_run_data_with_the_reply_still_metered()
    {
        var shortlist = new FakeShortlistRunService(new ShortlistRunOutcome(
            "shortlist", new AgentReply("x", 10, 5, 15), Response: null, FaultDetail: "retrieval down"));
        var match = new FakeMatchRunService((_, _) =>
            throw new InvalidOperationException("must not be called"));

        var outcome = await Service(shortlist, match).RunAsync("JD", topK: 3);

        outcome.FaultDetail.Should().Be("retrieval down");
        outcome.Results.Should().BeEmpty();
        match.Calls.Should().Be(0);
        outcome.Metered.Should().ContainSingle().Which.Step.Should().Be("jd-shortlist");
    }

    [Fact]
    public async Task TopK_is_clamped_and_passed_to_retrieval()
    {
        var shortlist = new FakeShortlistRunService(ShortlistOk(Candidate(Ada, "Ada", 0.9)));
        var match = new FakeMatchRunService((id, _) => Task.FromResult(MatchOutcome(id, 50, "Weak")));
        var service = Service(shortlist, match);

        await service.RunAsync("JD", topK: 99);
        await service.RunAsync("JD", topK: null);

        shortlist.Requests.Select(r => r.TopK).Should().Equal(JdMatchRunService.MaxTop, 3);
    }
}
