using CvManager.Agents.Agents;
using CvManager.Agents.Usage;
using CvManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CvManager.Agents.Tests;

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
