using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Configuration;
using ExpertToJob.Agents.Usage;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExpertToJob.Agents.Tests;

public class UsageMeterTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"usage-{Guid.NewGuid()}")
            .Options);

    private static UsageMeter Meter(AppDbContext db, ChatProvider provider = ChatProvider.Gemini) =>
        new(db, provider, TimeProvider.System, NullLogger<UsageMeter>.Instance);

    [Fact]
    public async Task RecordAsync_persists_a_row_with_tokens_and_the_replys_model()
    {
        await using var db = NewDb();
        var userId = Guid.NewGuid();

        await Meter(db).RecordAsync(
            userId, "match",
            new AgentReply("answer", 100, 40, 140, ModelId: "gemini-flash-lite-latest"));

        var row = await db.AgentUsages.SingleAsync();
        row.UserId.Should().Be(userId);
        row.AgentName.Should().Be("match");
        row.Model.Should().Be("gemini-flash-lite-latest");
        row.InputTokens.Should().Be(100);
        row.OutputTokens.Should().Be(40);
        row.TotalTokens.Should().Be(140);
    }

    /// <summary>
    /// Which backend served the run, on the row itself (EXP-19, ADR §2 decision 11). Recorded as
    /// the enum's own name — an enum at the config edge, a string in the database — so cost
    /// attribution never has to be inferred by parsing a model id, which drifts across aliases and
    /// version suffixes exactly when the attribution starts to matter.
    /// </summary>
    [Theory]
    [InlineData(ChatProvider.Gemini, "Gemini")]
    [InlineData(ChatProvider.AzureFoundry, "AzureFoundry")]
    public async Task Records_the_active_provider(ChatProvider provider, string expected)
    {
        await using var db = NewDb();

        await Meter(db, provider).RecordAsync(
            Guid.NewGuid(), "roster-qa", new AgentReply("a", 1, 1, 2, ModelId: "some-model"));

        (await db.AgentUsages.SingleAsync()).Provider.Should().Be(expected);
    }

    /// <summary>
    /// Pins the deletion of the config fallback. The meter used to fall back to
    /// <c>Ai:Gemini:Agents:&lt;agent&gt;</c> and <c>Ai:Gemini:Model</c> when a reply carried no model
    /// id, which — in its own comment's words — "mislabels whenever config and reality drift".
    /// A configuration-shaped dependency is exactly how that fallback would creep back, so the
    /// constructor is asserted not to take one.
    /// </summary>
    [Fact]
    public void Records_the_response_model_not_a_config_lookup()
    {
        typeof(UsageMeter).GetConstructors().Single()
            .GetParameters().Select(p => p.ParameterType)
            .Should().NotContain(typeof(Microsoft.Extensions.Configuration.IConfiguration),
                "a model id read from configuration is a label, not a measurement");
    }

    /// <summary>
    /// A reply that never reached a model records an empty model rather than a plausible-looking
    /// one. <c>Iterations = null</c> already encodes the same fact on the same row.
    /// </summary>
    [Fact]
    public async Task Records_an_empty_model_when_the_reply_never_reached_one()
    {
        await using var db = NewDb();

        await Meter(db).RecordAsync(Guid.NewGuid(), "match", new AgentReply("answer", 1, 1, 2));

        var row = await db.AgentUsages.SingleAsync();
        row.Model.Should().BeEmpty();
        row.Iterations.Should().BeNull();
        // The provider is known even when the model is not: it is configuration, not a measurement
        // of the call.
        row.Provider.Should().Be("Gemini");
    }

    [Fact]
    public async Task RecordAsync_stores_enrichment_alongside_the_real_model_id()
    {
        await using var db = NewDb();
        using var activity = new System.Diagnostics.Activity("test-request");
        activity.Start();

        await Meter(db).RecordAsync(
            Guid.NewGuid(), "staffing",
            new AgentReply(
                "answer", 10, 5, 15,
                ModelId: "gemini-2.5-flash-lite", LatencyMs: 1234,
                Iterations: 10, ToolSequence: "skill_list,cv_get"),
            step: "match");

        var row = await db.AgentUsages.SingleAsync();
        row.Model.Should().Be("gemini-2.5-flash-lite");
        row.LatencyMs.Should().Be(1234);
        row.Step.Should().Be("match");
        row.TraceId.Should().Be(activity.TraceId.ToString());
        // Why the call cost what it did, on the row itself (P1T-144) — no throwaway probe needed.
        row.Iterations.Should().Be(10);
        row.ToolSequence.Should().Be("skill_list,cv_get");
    }

    [Fact]
    public async Task RecordAsync_leaves_enrichment_null_when_nothing_was_captured()
    {
        await using var db = NewDb();

        await Meter(db).RecordAsync(Guid.NewGuid(), "roster-qa", new AgentReply("a", 1, 1, 2));

        var row = await db.AgentUsages.SingleAsync();
        row.LatencyMs.Should().BeNull();
        row.Step.Should().BeNull();
        // Zero iterations means the metering seam saw nothing, not "one cheap call".
        row.Iterations.Should().BeNull();
        row.ToolSequence.Should().BeNull();
    }
}
