using System.Threading.RateLimiting;
using CvManager.Agents.Agents;
using CvManager.Agents.RosterScan;
using CvManager.Application.Search;
using CvManager.Domain.Entities;
using CvManager.Agents.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;

namespace CvManager.Agents.Tests;

/// <summary>
/// QueuedSyncScoringTransport semantics (P1T-123) over a scripted chat client: the
/// schema-constrained request shape, the honest result-hygiene ladder (unknown ids dropped,
/// missing members failed, out-of-range scores nulled, unparseable reply fails the whole chunk
/// honestly), pacing through the shared limiter, and the 429 ladder (bounded retries, then the
/// typed quota exception the runner maps to paused(quota)).
/// </summary>
public class QueuedSyncScoringTransportTests
{
    private static readonly Guid Ada = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Grace = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Counts leases; never blocks — pacing behavior itself belongs to the real
    /// RateLimiter, the transport's contract is that it acquires one permit per model call.</summary>
    private sealed class CountingRateLimiter : RateLimiter
    {
        public int Acquired { get; private set; }

        public override TimeSpan? IdleDuration => null;

        public override RateLimiterStatistics? GetStatistics() => null;

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            Acquired += permitCount;
            return new Lease();
        }

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(
            int permitCount, CancellationToken cancellationToken)
        {
            Acquired += permitCount;
            return ValueTask.FromResult<RateLimitLease>(new Lease());
        }

        private sealed class Lease : RateLimitLease
        {
            public override bool IsAcquired => true;
            public override IEnumerable<string> MetadataNames => [];
            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                metadata = null;
                return false;
            }
        }
    }

    private static readonly IReadOnlyList<EmployeeDigest> Chunk =
    [
        new EmployeeDigest(Ada, "Ada Lovelace", "Engineer", "Built Kafka pipelines for 6 years."),
        new EmployeeDigest(Grace, "Grace Hopper", "Admiral", "Invented the compiler."),
    ];

    private static JdRequirements Extraction() => new(
        [new JdRequirement("kafka", RequirementKind.Skill, RequirementPriority.MustHave, null, "kafka", false)],
        JdSeniority.Senior, null, []);

    private static QueuedSyncScoringTransport Transport(
        FakeChatClient chat, CountingRateLimiter? limiter = null, RosterScanOptions? options = null) =>
        new(chat, limiter ?? new CountingRateLimiter(),
            options ?? new RosterScanOptions { RetryBaseSeconds = 0 },
            new FakeTimeProvider());

    private static ChatResponse Reply(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails { InputTokenCount = 200, OutputTokenCount = 50, TotalTokenCount = 250 },
            ModelId = "gemini-3.5-flash-lite",
        };

    private static string BothAssessed() =>
        $$"""
        {"assessments":[
          {"employeeId":"{{Ada}}","score":85,"band":"Strong","rationale":"Kafka depth.","scorable":true},
          {"employeeId":"{{Grace}}","score":null,"band":"InsufficientEvidence","rationale":"No requirement evidence.","scorable":false}]}
        """;

    [Fact]
    public async Task Requests_the_schema_and_carries_digests_and_extraction_in_the_prompt()
    {
        var chat = new FakeChatClient(() => Reply(BothAssessed()));
        var transport = Transport(chat);

        await transport.ScoreChunkAsync("Senior Kafka engineer JD", Extraction(), Chunk);

        chat.ReceivedOptions[0]!.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>()
            .Which.Schema.Should().NotBeNull();
        chat.ReceivedOptions[0]!.Tools.Should().BeNullOrEmpty("the scoring call is tool-less");
        var prompt = string.Concat(chat.ReceivedMessages[0].Select(m => m.Text));
        prompt.Should().Contain("Senior Kafka engineer JD")
            .And.Contain("Extracted role requirements")
            .And.Contain("Built Kafka pipelines")
            .And.Contain(Ada.ToString());
    }

    [Fact]
    public async Task Maps_assessments_to_scored_results_with_display_bands_and_meters_the_reply()
    {
        var transport = Transport(new FakeChatClient(() => Reply(BothAssessed())));

        var scored = await transport.ScoreChunkAsync("JD", null, Chunk);

        var ada = scored.Results.Single(r => r.EmployeeId == Ada);
        ada.Status.Should().Be(ScoringCandidateStatus.Scored);
        ada.Score.Should().Be(85);
        ada.Band.Should().Be("Strong");
        var grace = scored.Results.Single(r => r.EmployeeId == Grace);
        grace.Score.Should().BeNull();
        grace.Band.Should().Be("Insufficient evidence", "display bands keep the parser-era strings");
        grace.Scorable.Should().BeFalse();
        scored.Reply.TotalTokens.Should().Be(250);
        scored.Reply.ModelId.Should().Be("gemini-3.5-flash-lite");
    }

    [Fact]
    public async Task Unknown_ids_are_dropped_and_missing_members_fail_honestly()
    {
        var stranger = Guid.NewGuid();
        var transport = Transport(new FakeChatClient(() => Reply(
            $$"""
            {"assessments":[
              {"employeeId":"{{Ada}}","score":70,"band":"Moderate","rationale":"ok","scorable":true},
              {"employeeId":"{{stranger}}","score":99,"band":"Strong","rationale":"??","scorable":true}]}
            """)));

        var scored = await transport.ScoreChunkAsync("JD", null, Chunk);

        scored.Results.Should().HaveCount(2, "one row per chunk member, never per model claim");
        scored.Results.Select(r => r.EmployeeId).Should().BeEquivalentTo([Ada, Grace]);
        scored.Results.Single(r => r.EmployeeId == Grace).Status.Should().Be(ScoringCandidateStatus.Failed);
        scored.Results.Single(r => r.EmployeeId == Grace).Error.Should().Contain("did not assess");
    }

    [Fact]
    public async Task Out_of_range_scores_are_nulled_not_trusted()
    {
        var transport = Transport(new FakeChatClient(() => Reply(
            $$"""{"assessments":[{"employeeId":"{{Ada}}","score":780,"band":"Strong","rationale":"ok","scorable":true}]}""")));

        var scored = await transport.ScoreChunkAsync("JD", null, [Chunk[0]]);

        scored.Results.Single().Score.Should().BeNull();
        scored.Results.Single().Band.Should().Be("Strong");
    }

    [Fact]
    public async Task Unparseable_reply_fails_the_whole_chunk_with_the_reply_still_metered()
    {
        var transport = Transport(new FakeChatClient(() => Reply("Here is an essay instead.")));

        var scored = await transport.ScoreChunkAsync("JD", null, Chunk);

        scored.Results.Should().OnlyContain(r => r.Status == ScoringCandidateStatus.Failed);
        scored.Results.Should().OnlyContain(r => r.Error!.Contains("did not parse"));
        scored.Reply.TotalTokens.Should().Be(250, "tokens were spent either way");
    }

    [Fact]
    public async Task Acquires_one_permit_per_model_call_including_retries()
    {
        var limiter = new CountingRateLimiter();
        var chat = new FakeChatClient(
            () => throw new HttpRequestException("rate limited", null, System.Net.HttpStatusCode.TooManyRequests),
            () => Reply(BothAssessed()));

        var scored = await Transport(chat, limiter).ScoreChunkAsync("JD", null, Chunk);

        scored.Results.Should().Contain(r => r.Status == ScoringCandidateStatus.Scored);
        chat.CallCount.Should().Be(2, "one 429 then success");
        limiter.Acquired.Should().Be(2, "each attempt takes its own permit");
    }

    [Fact]
    public async Task Exhausted_retry_budget_surfaces_the_typed_quota_exception()
    {
        var chat = new FakeChatClient(
            () => throw new HttpRequestException("rate limited", null, System.Net.HttpStatusCode.TooManyRequests));
        var transport = Transport(chat, options: new RosterScanOptions { MaxRetryAttempts = 3, RetryBaseSeconds = 0 });

        var act = () => transport.ScoreChunkAsync("JD", null, Chunk);

        await act.Should().ThrowAsync<ScoringQuotaExceededException>();
        chat.CallCount.Should().Be(3, "the budget is attempts, not retries-after-first");
    }

    [Fact]
    public async Task Non_rate_limit_faults_propagate_untouched()
    {
        var chat = new FakeChatClient(() => throw new HttpRequestException("model endpoint down"));

        var act = () => Transport(chat).ScoreChunkAsync("JD", null, Chunk);

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("model endpoint down");
        chat.CallCount.Should().Be(1, "only 429s retry");
    }
}
