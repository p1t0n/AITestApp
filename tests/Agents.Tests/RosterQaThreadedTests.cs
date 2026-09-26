using System.Net.Http.Json;
using System.Text.Json;
using ExpertToJob.Agents.Mcp;
using ExpertToJob.Agents.Tests.Fakes;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The threaded contract from issue 0016 / P1T-82, now over a durable store (EXP-36): a follow-up
/// runs with the prior turn's question and answer replayed ahead of it, so the model actually sees
/// the context.
///
/// <para>Over the endpoint and over real Postgres, because what changed is exactly the part a
/// unit test cannot reach. The old store was a dictionary, and "lost on restart" was its documented
/// behaviour; the claim now is the opposite one, so the test restarts the host — a second
/// <see cref="WebApplicationFactory{T}"/> against the same database — and asks again.</para>
/// </summary>
public sealed class RosterQaThreadedTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task A_followup_replays_the_prior_turn_to_the_model()
    {
        var userId = Guid.NewGuid();
        var chat = Answers("Ada Lovelace knows React.", "Ada is free in July.");
        await using var host = NewHost(chat);
        using var client = host.CreateAuthenticatedClient(userId);

        var first = await AskAsync(client, "Who knows React?");
        await AskAsync(client, "And which of them are free in July?", first.ThreadId);

        var secondTurn = chat.ReceivedMessages[^1];
        secondTurn.Select(m => m.Text).Should().ContainInOrder(
            "Who knows React?",
            "Ada Lovelace knows React.",
            "And which of them are free in July?");
        secondTurn.First(m => m.Text == "Ada Lovelace knows React.").Role
            .Should().Be(ChatRole.Assistant, "the prior answer replays as an assistant turn");
    }

    [Fact]
    public async Task The_first_question_of_a_conversation_sends_only_itself()
    {
        var chat = Answers("answer");
        await using var host = NewHost(chat);
        using var client = host.CreateAuthenticatedClient(Guid.NewGuid());

        await AskAsync(client, "Who knows React?");

        chat.ReceivedMessages[^1].Count(m => m.Role == ChatRole.User).Should().Be(1);
    }

    /// <summary>
    /// The reversal this slice exists for. <c>RosterQaThreadStore</c> documented its history as
    /// "lost on restart by design"; a conversation is rows now, so a new host over the same
    /// database resumes it.
    /// </summary>
    [Fact]
    public async Task A_conversation_survives_the_host_that_started_it()
    {
        var userId = Guid.NewGuid();
        string threadId;

        var before = Answers("Ada Lovelace knows React.");
        await using (var host = NewHost(before))
        {
            using var client = host.CreateAuthenticatedClient(userId);
            threadId = (await AskAsync(client, "Who knows React?")).ThreadId;
        }

        var after = Answers("Ada is free in July.");
        await using (var restarted = NewHost(after))
        {
            using var client = restarted.CreateAuthenticatedClient(userId);
            var resumed = await AskAsync(client, "And which of them are free in July?", threadId);

            resumed.ThreadId.Should().Be(threadId, "the conversation is the same one, on a new process");
        }

        after.ReceivedMessages[^1].Select(m => m.Text).Should().ContainInOrder(
            "Who knows React?",
            "Ada Lovelace knows React.",
            "And which of them are free in July?");
    }

    [Fact]
    public async Task An_unknown_conversation_id_answers_with_a_new_one_and_no_replayed_context()
    {
        var chat = Answers("Ada Lovelace knows React.");
        await using var host = NewHost(chat);
        using var client = host.CreateAuthenticatedClient(Guid.NewGuid());
        var stale = Guid.NewGuid().ToString();

        var reply = await AskAsync(client, "Who knows React?", stale);

        reply.ThreadId.Should().NotBe(stale,
            "the id changing is the SPA's 'conversation expired' signal, and it must keep working");
        chat.ReceivedMessages[^1].Count(m => m.Role == ChatRole.User).Should().Be(1);
    }

    [Fact]
    public async Task One_owners_conversation_id_replays_nothing_for_another()
    {
        var chat = Answers("Ada Lovelace knows React.", "Nothing to go on.");
        await using var host = NewHost(chat);

        using var alice = host.CreateAuthenticatedClient(Guid.NewGuid());
        var hers = (await AskAsync(alice, "Who knows React?")).ThreadId;

        using var bob = host.CreateAuthenticatedClient(Guid.NewGuid());
        var his = await AskAsync(bob, "What did she just ask?", hers);

        his.ThreadId.Should().NotBe(hers);
        chat.ReceivedMessages[^1].Select(m => m.Text).Should().NotContain("Who knows React?");
    }

    // ---- plumbing -----------------------------------------------------------------------------

    private sealed record RosterQaBody(string Answer, string ThreadId);

    private static async Task<RosterQaBody> AskAsync(HttpClient client, string question, string? threadId = null)
    {
        var response = await client.PostAsJsonAsync("/agents/roster-qa", new { question, threadId });
        response.EnsureSuccessStatusCode();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return new RosterQaBody(body.GetProperty("answer").GetString()!, body.GetProperty("threadId").GetString()!);
    }

    private static FakeChatClient Answers(params string[] texts) =>
        new(texts.Select(t => (Func<ChatResponse>)(() =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, t)))).ToArray());

    private WebApplicationFactory<Program> NewHost(IChatClient chat) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
            b.ConfigureServices(s =>
            {
                s.AddSingleton(chat);
                s.AddKeyedSingleton<IMcpToolSource>("roster-qa", (_, _) => new FakeToolSource());
            });
        });
}
