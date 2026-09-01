using ExpertToJob.Agents.Agents;
using ExpertToJob.Agents.Usage;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExpertToJob.Agents.Tests;

public class UsageMeterTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"usage-{Guid.NewGuid()}")
            .Options);

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public async Task RecordAsync_persists_a_row_with_tokens_and_resolved_model()
    {
        await using var db = NewDb();
        var meter = new UsageMeter(
            db,
            Config(("Gemini:Model", "gemini-flash-lite-latest")),
            TimeProvider.System,
            NullLogger<UsageMeter>.Instance);
        var userId = Guid.NewGuid();

        await meter.RecordAsync(userId, "match", new AgentReply("answer", 100, 40, 140));

        var row = await db.AgentUsages.SingleAsync();
        row.UserId.Should().Be(userId);
        row.AgentName.Should().Be("match");
        row.Model.Should().Be("gemini-flash-lite-latest");
        row.InputTokens.Should().Be(100);
        row.OutputTokens.Should().Be(40);
        row.TotalTokens.Should().Be(140);
    }

    [Fact]
    public async Task RecordAsync_prefers_the_replys_real_model_id_over_config_and_stores_enrichment()
    {
        await using var db = NewDb();
        var meter = new UsageMeter(
            db,
            Config(("Gemini:Model", "configured-model")),
            TimeProvider.System,
            NullLogger<UsageMeter>.Instance);
        using var activity = new System.Diagnostics.Activity("test-request");
        activity.Start();

        await meter.RecordAsync(
            Guid.NewGuid(), "staffing",
            new AgentReply(
                "answer", 10, 5, 15,
                ModelId: "gemini-2.5-flash-lite", LatencyMs: 1234,
                Iterations: 10, ToolSequence: "skill_list,cv_get"),
            step: "match");

        var row = await db.AgentUsages.SingleAsync();
        row.Model.Should().Be("gemini-2.5-flash-lite", "the response's real id beats the config label");
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
        var meter = new UsageMeter(
            db, Config(("Gemini:Model", "m")), TimeProvider.System, NullLogger<UsageMeter>.Instance);

        await meter.RecordAsync(Guid.NewGuid(), "roster-qa", new AgentReply("a", 1, 1, 2));

        var row = await db.AgentUsages.SingleAsync();
        row.LatencyMs.Should().BeNull();
        row.Step.Should().BeNull();
        // Zero iterations means the metering seam saw nothing, not "one cheap call".
        row.Iterations.Should().BeNull();
        row.ToolSequence.Should().BeNull();
    }

    [Fact]
    public async Task RecordAsync_prefers_the_per_agent_model_override()
    {
        await using var db = NewDb();
        var meter = new UsageMeter(
            db,
            Config(("Gemini:Model", "gemini-flash-lite-latest"),
                   ("Gemini:Agents:match", "gemini-pro-latest")),
            TimeProvider.System,
            NullLogger<UsageMeter>.Instance);

        await meter.RecordAsync(Guid.NewGuid(), "match", new AgentReply("answer", 1, 1, 2));

        (await db.AgentUsages.SingleAsync()).Model.Should().Be("gemini-pro-latest");
    }
}
