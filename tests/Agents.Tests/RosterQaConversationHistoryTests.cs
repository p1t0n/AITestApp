using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpertToJob.Agents.Mcp;
using ExpertToJob.Agents.Tests.Fakes;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The history API (EXP-33, <c>manuals/adr-roster-qa-conversation-history.md</c> §5 and §7): an
/// owner lists their conversations, reads one back, and deletes one or all of them.
///
/// <para>Over the endpoints and over real Postgres, because every claim here is about <em>whose</em>
/// rows come back. The owner scope is a <c>WHERE UserId = …</c> the database runs, the pause mask
/// is a correlated <c>EXISTS</c> over <c>Experts</c> the provider has to translate, and the delete
/// reaches the turns and their touched rows only by the configured <c>ON DELETE CASCADE</c>. None
/// of the three is observable against an in-memory provider.</para>
///
/// <para>Someone else's conversation is <b>404, never 403</b>. A 403 is an answer: it says the id
/// names something real and it is not yours. The routes take no user id at all, so the only
/// conversation a caller can name is one they hold the id for — and a guessed id must read exactly
/// like a wrong one.</para>
/// </summary>
public sealed class RosterQaConversationHistoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        _host = NewHost();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // ---- list ---------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_is_only_the_callers_own_newest_activity_first()
    {
        var alice = SeedOwner();
        var bob = SeedOwner();
        var older = await SeedConversationAsync(alice, "Who knows COBOL?", lastActiveAt: Now.AddDays(-3));
        var newer = await SeedConversationAsync(alice, "Who knows React?", lastActiveAt: Now.AddDays(-1));
        var his = await SeedConversationAsync(bob, "His own question");

        var list = await ListAsync(alice);

        list.Select(c => c.GetProperty("id").GetGuid()).Should().Equal(newer, older);
        list.Select(c => c.GetProperty("title").GetString()).Should().Equal(
            "Who knows React?", "Who knows COBOL?");
        list.Should().NotContain(c => c.GetProperty("id").GetGuid() == his);
    }

    [Fact]
    public async Task A_row_carries_when_it_started_when_it_was_last_active_and_when_it_expires()
    {
        var alice = SeedOwner();
        await SeedConversationAsync(
            alice, "Who knows React?",
            createdAt: new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero),
            lastActiveAt: new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero));

        var row = (await ListAsync(alice)).Single();

        row.GetProperty("createdAt").GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 1, 2, 8, 0, 0, TimeSpan.Zero));
        row.GetProperty("lastActiveAt").GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero));
        // Six calendar months past the last turn, which is the moment the retention sweep may take
        // it (ADR §6). 180 days would land on 14 July — the literal is here so the two cannot be
        // confused by a cutoff computed the way the code computes it.
        row.GetProperty("expiresAt").GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 7, 15, 9, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_caller_with_no_conversations_gets_an_empty_list_rather_than_an_error()
    {
        var alice = SeedOwner();

        (await ListAsync(alice)).Should().BeEmpty();
    }

    // ---- get ----------------------------------------------------------------------------------

    [Fact]
    public async Task Get_returns_the_turns_oldest_first_with_everything_the_dock_renders()
    {
        var alice = SeedOwner();
        var conversation = await SeedConversationAsync(
            alice, "Who knows React?", lastActiveAt: new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero));
        await SeedTurnAsync(conversation, "Who knows React?", "Ada Lovelace does.", Now.AddMinutes(1));
        await SeedTurnAsync(
            conversation, "And COBOL?", "Grace Hopper does.", Now.AddMinutes(2),
            modelId: "gemini-3.5-flash-lite-002", grounded: false);

        var detail = await GetAsync(alice, conversation);

        detail.GetProperty("id").GetGuid().Should().Be(conversation);
        detail.GetProperty("title").GetString().Should().Be("Who knows React?");
        detail.GetProperty("expiresAt").GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 7, 15, 9, 30, 0, TimeSpan.Zero));

        var turns = detail.GetProperty("turns").EnumerateArray().ToList();
        turns.Select(t => t.GetProperty("question").GetString())
            .Should().Equal("Who knows React?", "And COBOL?");
        turns.Select(t => t.GetProperty("answer").GetString())
            .Should().Equal("Ada Lovelace does.", "Grace Hopper does.");
        turns.Select(t => t.GetProperty("state").GetString()).Should().AllBe("ok");
        turns[1].GetProperty("modelId").GetString().Should().Be("gemini-3.5-flash-lite-002");
        turns[1].GetProperty("grounded").GetBoolean().Should().BeFalse();
        turns[0].GetProperty("createdAt").GetDateTimeOffset().Should().Be(Now.AddMinutes(1));
    }

    /// <summary>
    /// The pause mask (ADR §5), computed at read time and never written down: the turn reads
    /// <c>hidden</c> with both texts empty while the Expert is paused, and comes straight back when
    /// they are not. A masked turn still holds its place in the transcript — the dock shows one
    /// muted line rather than a gap it cannot explain.
    /// </summary>
    [Fact]
    public async Task A_turn_touching_a_paused_expert_reads_hidden_with_empty_texts_and_returns_on_unpause()
    {
        var alice = SeedOwner();
        var ada = await SeedExpertAsync();
        var conversation = await SeedConversationAsync(alice, "Who knows React?");
        await SeedTurnAsync(conversation, "Who knows React?", "Ada Lovelace does.", Now.AddMinutes(1), touching: [ada]);
        await SeedTurnAsync(conversation, "How many experts?", "Forty.", Now.AddMinutes(2));

        await PauseAsync(ada, paused: true);

        var masked = (await GetAsync(alice, conversation)).GetProperty("turns").EnumerateArray().ToList();
        masked.Select(t => t.GetProperty("state").GetString()).Should().Equal("hidden", "ok");
        masked[0].GetProperty("question").GetString().Should().BeEmpty();
        masked[0].GetProperty("answer").GetString().Should().BeEmpty();
        masked[1].GetProperty("answer").GetString().Should().Be("Forty.");

        await PauseAsync(ada, paused: false);

        var restored = (await GetAsync(alice, conversation)).GetProperty("turns").EnumerateArray().ToList();
        restored.Select(t => t.GetProperty("state").GetString()).Should().AllBe("ok");
        restored[0].GetProperty("answer").GetString().Should().Be("Ada Lovelace does.",
            "unpausing costs nothing — the text was never rewritten");
    }

    [Fact]
    public async Task A_turn_is_hidden_when_any_one_of_its_touched_experts_is_paused()
    {
        var alice = SeedOwner();
        var ada = await SeedExpertAsync();
        var grace = await SeedExpertAsync();
        var conversation = await SeedConversationAsync(alice, "Who is on the bench?");
        await SeedTurnAsync(conversation, "Who is on the bench?", "Ada and Grace.", Now.AddMinutes(1), touching: [ada, grace]);

        await PauseAsync(grace, paused: true);

        var turn = (await GetAsync(alice, conversation)).GetProperty("turns").EnumerateArray().Single();
        turn.GetProperty("state").GetString().Should().Be("hidden");
        turn.GetProperty("answer").GetString().Should().BeEmpty();
    }

    /// <summary>
    /// The erasure scrub's side (ADR §5): a <c>Removed</c> turn was emptied when an Expert it named
    /// was erased, and no unpause brings it back. It reads as its own state, not as
    /// <c>hidden</c> — the difference is permanent versus reversible, and the dock says which.
    /// </summary>
    [Fact]
    public async Task A_removed_turn_reads_removed_with_empty_texts()
    {
        var alice = SeedOwner();
        var conversation = await SeedConversationAsync(alice, "Who knows React?");
        await SeedTurnAsync(conversation, "", "", Now.AddMinutes(1), state: RosterQaTurnState.Removed);
        await SeedTurnAsync(conversation, "How many experts?", "Forty.", Now.AddMinutes(2));

        var turns = (await GetAsync(alice, conversation)).GetProperty("turns").EnumerateArray().ToList();

        turns.Select(t => t.GetProperty("state").GetString()).Should().Equal("removed", "ok");
        turns[0].GetProperty("question").GetString().Should().BeEmpty();
        turns[0].GetProperty("answer").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task Another_owners_conversation_is_not_found_rather_than_forbidden_and_is_left_alone()
    {
        var alice = SeedOwner();
        var bob = SeedOwner();
        var hers = await SeedConversationAsync(alice, "Who knows React?");
        await SeedTurnAsync(hers, "Who knows React?", "Ada Lovelace does.", Now.AddMinutes(1));

        using var client = Client(bob);
        (await client.GetAsync($"/agents/roster-qa/conversations/{hers}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "a 403 would confirm the id names something real");
        (await client.DeleteAsync($"/agents/roster-qa/conversations/{hers}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        await using var db = NewDb();
        (await db.RosterQaConversations.AnyAsync(c => c.Id == hers)).Should().BeTrue();
        (await db.RosterQaTurns.CountAsync(t => t.ConversationId == hers)).Should().Be(1);
    }

    [Fact]
    public async Task An_id_that_names_nothing_is_not_found_on_get_and_on_delete()
    {
        var alice = SeedOwner();
        using var client = Client(alice);
        var nobodys = Guid.NewGuid();

        (await client.GetAsync($"/agents/roster-qa/conversations/{nobodys}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/agents/roster-qa/conversations/{nobodys}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // ---- delete -------------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_one_conversation_takes_its_turns_and_touched_rows_and_nothing_else()
    {
        var alice = SeedOwner();
        var ada = await SeedExpertAsync();
        var doomed = await SeedConversationAsync(alice, "Who knows React?");
        var turnId = await SeedTurnAsync(doomed, "Who knows React?", "Ada does.", Now.AddMinutes(1), touching: [ada]);
        var kept = await SeedConversationAsync(alice, "Who knows COBOL?");
        await SeedTurnAsync(kept, "Who knows COBOL?", "Grace does.", Now.AddMinutes(2));

        using var client = Client(alice);
        (await client.DeleteAsync($"/agents/roster-qa/conversations/{doomed}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        await using var db = NewDb();
        (await db.RosterQaConversations.AnyAsync(c => c.Id == doomed)).Should().BeFalse();
        (await db.RosterQaTurns.AnyAsync(t => t.ConversationId == doomed)).Should().BeFalse();
        (await db.RosterQaTurnExperts.AnyAsync(x => x.TurnId == turnId))
            .Should().BeFalse("the touched rows go by cascade");
        (await db.RosterQaConversations.AnyAsync(c => c.Id == kept)).Should().BeTrue();
        (await db.RosterQaTurns.CountAsync(t => t.ConversationId == kept)).Should().Be(1);
        (await db.Experts.AnyAsync(e => e.Id == ada))
            .Should().BeTrue("deleting a transcript never touches the people it mentioned");
    }

    [Fact]
    public async Task Deleting_everything_takes_all_of_the_callers_and_none_of_anyone_elses()
    {
        var alice = SeedOwner();
        var bob = SeedOwner();
        await SeedConversationAsync(alice, "Who knows React?");
        await SeedConversationAsync(alice, "Who knows COBOL?");
        var his = await SeedConversationAsync(bob, "His own question");
        await SeedTurnAsync(his, "His own question", "His own answer", Now.AddMinutes(1));

        using var client = Client(alice);
        (await client.DeleteAsync("/agents/roster-qa/conversations")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        await using var db = NewDb();
        (await db.RosterQaConversations.CountAsync(c => c.UserId == alice)).Should().Be(0);
        (await db.RosterQaConversations.CountAsync(c => c.UserId == bob)).Should().Be(1);
        (await db.RosterQaTurns.CountAsync(t => t.ConversationId == his)).Should().Be(1);
    }

    [Fact]
    public async Task Deleting_everything_when_there_is_nothing_still_succeeds()
    {
        var alice = SeedOwner();
        using var client = Client(alice);

        (await client.DeleteAsync("/agents/roster-qa/conversations")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// The route the dock actually walks: ask, then find the answer in the history. This is the one
    /// test that ties the write path (EXP-36) to the read path, so a change to either that stops
    /// them agreeing fails here rather than in a browser.
    /// </summary>
    [Fact]
    public async Task A_question_just_asked_is_readable_in_the_history_it_created()
    {
        var alice = SeedOwner();
        using var client = Client(alice);

        var posted = await client.PostAsJsonAsync(
            "/agents/roster-qa", new { question = "Who knows React?" });
        posted.EnsureSuccessStatusCode();
        var threadId = Guid.Parse(JsonDocument.Parse(await posted.Content.ReadAsStringAsync())
            .RootElement.GetProperty("threadId").GetString()!);

        (await ListAsync(alice)).Single().GetProperty("id").GetGuid().Should().Be(threadId);

        var turn = (await GetAsync(alice, threadId)).GetProperty("turns").EnumerateArray().Single();
        turn.GetProperty("question").GetString().Should().Be("Who knows React?");
        turn.GetProperty("answer").GetString().Should().Be("Ada Lovelace knows React.");
        turn.GetProperty("state").GetString().Should().Be("ok");
    }

    // ---- plumbing -----------------------------------------------------------------------------

    private HttpClient Client(Guid userId) => _host.CreateAuthenticatedClient(userId);

    private async Task<List<JsonElement>> ListAsync(Guid userId)
    {
        using var client = Client(userId);
        var response = await client.GetAsync("/agents/roster-qa/conversations");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.EnumerateArray().ToList();
    }

    private async Task<JsonElement> GetAsync(Guid userId, Guid conversationId)
    {
        using var client = Client(userId);
        var response = await client.GetAsync($"/agents/roster-qa/conversations/{conversationId}");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private WebApplicationFactory<Program> NewHost() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
            // The sweep would run against the same database; this suite's fixtures are dated
            // relative to "now" and nothing here is testing retention.
            b.UseSetting("ConversationRetention:Enabled", "false");
            b.ConfigureServices(s =>
            {
                s.AddSingleton<Microsoft.Extensions.AI.IChatClient>(new FakeChatClient(
                    () => new Microsoft.Extensions.AI.ChatResponse(new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant, "Ada Lovelace knows React."))));
                s.AddKeyedSingleton<IMcpToolSource>("roster-qa", (_, _) => new FakeToolSource());
            });
        });

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
        .Options);

    /// <summary>An account the history can belong to. Every route is scoped to the caller's own
    /// <c>UserId</c>, so "whose" is the only variable most of these tests move.</summary>
    private Guid SeedOwner() => _host.EnsureAccount(Guid.NewGuid()).Id;

    private async Task<Guid> SeedExpertAsync()
    {
        await using var db = NewDb();
        var expert = new Expert
        {
            Id = Guid.NewGuid(),
            FirstName = "Ada",
            LastName = "Lovelace",
            Title = "Engineer",
            Email = $"ada-{Guid.NewGuid():N}@example.com",
        };
        db.Experts.Add(expert);
        await db.SaveChangesAsync();
        return expert.Id;
    }

    private async Task PauseAsync(Guid expertId, bool paused)
    {
        await using var db = NewDb();
        var expert = await db.Experts.SingleAsync(e => e.Id == expertId);
        expert.HiddenAt = paused ? Now : null;
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedConversationAsync(
        Guid ownerId, string title, DateTimeOffset? createdAt = null, DateTimeOffset? lastActiveAt = null)
    {
        await using var db = NewDb();
        var conversation = new RosterQaConversation
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            CreatedAt = createdAt ?? Now,
            LastActiveAt = lastActiveAt ?? Now,
            Title = title,
        };
        db.RosterQaConversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation.Id;
    }

    private async Task<Guid> SeedTurnAsync(
        Guid conversationId, string question, string answer, DateTimeOffset createdAt,
        RosterQaTurnState state = RosterQaTurnState.Ok, string modelId = "gemini-3.5-flash-lite",
        bool grounded = true, Guid[]? touching = null)
    {
        await using var db = NewDb();
        var turn = new RosterQaTurn
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            QuestionText = question,
            AnswerText = answer,
            ModelId = modelId,
            Grounded = grounded,
            State = state,
            CreatedAt = createdAt,
        };
        foreach (var expertId in touching ?? [])
        {
            turn.TouchedExperts.Add(new RosterQaTurnExpert { TurnId = turn.Id, ExpertId = expertId });
        }

        db.RosterQaTurns.Add(turn);
        await db.SaveChangesAsync();
        return turn.Id;
    }
}
