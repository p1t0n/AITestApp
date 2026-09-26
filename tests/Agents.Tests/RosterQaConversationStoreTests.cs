using ExpertToJob.Agents.Agents;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The durable conversation store that replaced <c>RosterQaThreadStore</c> (EXP-36,
/// <c>manuals/adr-roster-qa-conversation-history.md</c> §3–§5). The in-memory store's lifetime
/// rules — a 30-minute sliding TTL, an LRU cap of 20 threads — are gone on purpose: age is
/// retention's job now, and a conversation survives a restart.
///
/// <para>Against real Postgres, because two of these are only true there. The replay window's
/// pause filter is a correlated <c>EXISTS</c> over <c>Experts</c> that the provider has to
/// translate, and "two concurrent appends both persist" is a claim about two connections, which
/// the in-memory provider cannot make.</para>
/// </summary>
public sealed class RosterQaConversationStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    private static readonly DateTimeOffset Now = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ---- resume -------------------------------------------------------------------------------

    [Fact]
    public async Task A_missing_id_starts_a_fresh_empty_conversation_that_is_not_written_until_a_turn_lands()
    {
        var alice = await SeedUserAsync();

        var resolved = await ResolveAsync(alice, threadId: null);

        resolved.Id.Should().NotBeEmpty();
        resolved.History.Should().BeEmpty();
        resolved.IsNew.Should().BeTrue();

        await using var db = NewDb();
        (await db.RosterQaConversations.AnyAsync(c => c.Id == resolved.Id))
            .Should().BeFalse("a question that never got answered leaves no conversation behind");
    }

    [Fact]
    public async Task Resume_replays_the_last_ten_ok_turns_oldest_first()
    {
        var alice = await SeedUserAsync();
        var conversation = await SeedConversationAsync(alice);
        for (var i = 1; i <= 12; i++)
        {
            await SeedTurnAsync(conversation, $"q{i}", $"a{i}", Now.AddMinutes(i));
        }

        var resolved = await ResolveAsync(alice, conversation.ToString());

        resolved.Id.Should().Be(conversation);
        resolved.History.Should().HaveCount(20, "ten turns, each a user/assistant pair");
        resolved.History[0].Text.Should().Be("q3", "the two oldest turns fall outside the window");
        resolved.History[0].Role.Should().Be(ChatRole.User);
        resolved.History[1].Role.Should().Be(ChatRole.Assistant);
        resolved.History[^1].Text.Should().Be("a12");
    }

    [Fact]
    public async Task A_removed_turn_is_never_replayed()
    {
        var alice = await SeedUserAsync();
        var conversation = await SeedConversationAsync(alice);
        await SeedTurnAsync(conversation, "who knows COBOL?", "Grace Hopper does.", Now.AddMinutes(1));
        await SeedTurnAsync(conversation, "", "", Now.AddMinutes(2), state: RosterQaTurnState.Removed);
        await SeedTurnAsync(conversation, "and React?", "Ada Lovelace does.", Now.AddMinutes(3));

        var resolved = await ResolveAsync(alice, conversation.ToString());

        resolved.History.Select(m => m.Text).Should().Equal(
            "who knows COBOL?", "Grace Hopper does.", "and React?", "Ada Lovelace does.");
    }

    /// <summary>
    /// The pause mask (ADR §5). A pause is reversible and free, so the turn is filtered at read
    /// time and never rewritten — unpausing brings it straight back.
    /// </summary>
    [Fact]
    public async Task A_turn_touching_a_paused_expert_drops_out_of_the_window_and_comes_back_on_unpause()
    {
        var alice = await SeedUserAsync();
        var ada = await SeedExpertAsync();
        var conversation = await SeedConversationAsync(alice);
        await SeedTurnAsync(conversation, "who knows React?", "Ada Lovelace does.", Now.AddMinutes(1), touching: ada);
        await SeedTurnAsync(conversation, "how many experts?", "Forty.", Now.AddMinutes(2));

        (await ResolveAsync(alice, conversation.ToString())).History.Should().HaveCount(4);

        await PauseAsync(ada, paused: true);

        var masked = await ResolveAsync(alice, conversation.ToString());
        masked.History.Select(m => m.Text).Should().Equal("how many experts?", "Forty.");

        await PauseAsync(ada, paused: false);

        (await ResolveAsync(alice, conversation.ToString())).History.Should().HaveCount(4,
            "unpausing restores the turn at no cost — nothing was rewritten");
    }

    [Fact]
    public async Task A_turn_is_masked_when_any_one_of_its_touched_experts_is_paused()
    {
        var alice = await SeedUserAsync();
        var ada = await SeedExpertAsync();
        var grace = await SeedExpertAsync();
        var conversation = await SeedConversationAsync(alice);
        await SeedTurnAsync(conversation, "who is on the bench?", "Ada and Grace.", Now.AddMinutes(1), ada, grace);

        await PauseAsync(grace, paused: true);

        (await ResolveAsync(alice, conversation.ToString())).History.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_owners_id_starts_a_fresh_conversation_and_leaks_none_of_their_turns()
    {
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        var hers = await SeedConversationAsync(alice);
        await SeedTurnAsync(hers, "secret question", "secret answer", Now.AddMinutes(1));

        var resolved = await ResolveAsync(bob, hers.ToString());

        resolved.Id.Should().NotBe(hers, "the id changing is how the client learns the context is gone");
        resolved.History.Should().BeEmpty();
        resolved.IsNew.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_or_unparseable_id_starts_a_fresh_conversation()
    {
        var alice = await SeedUserAsync();

        (await ResolveAsync(alice, Guid.NewGuid().ToString())).History.Should().BeEmpty();
        (await ResolveAsync(alice, "not-a-guid")).History.Should().BeEmpty();
        (await ResolveAsync(alice, "not-a-guid")).IsNew.Should().BeTrue();
    }

    /// <summary>
    /// The lifetime rule the in-memory store had and this one does not. Age is retention's job
    /// (ADR §4/§6): six months of silence deletes a conversation, and thirty-one minutes does
    /// nothing at all.
    /// </summary>
    [Fact]
    public async Task There_is_no_idle_timeout_any_more()
    {
        var alice = await SeedUserAsync();
        var conversation = await SeedConversationAsync(alice, lastActiveAt: Now.AddMonths(-5));
        await SeedTurnAsync(conversation, "q", "a", Now.AddMonths(-5));

        var resolved = await ResolveAsync(alice, conversation.ToString(), now: Now);

        resolved.Id.Should().Be(conversation);
        resolved.History.Should().HaveCount(2, "five months idle is well inside the six-month promise");
    }

    // ---- append -------------------------------------------------------------------------------

    [Fact]
    public async Task The_first_append_writes_the_conversation_the_turn_and_its_touched_experts()
    {
        var alice = await SeedUserAsync();
        var ada = await SeedExpertAsync();
        var resolved = await ResolveAsync(alice, threadId: null);

        await AppendAsync(alice, resolved, "Who knows React?", Reply(
            "Ada Lovelace knows React.", modelId: "gemini-3.5-flash-lite-002", grounded: true, touched: [ada]));

        await using var db = NewDb();
        var conversation = await db.RosterQaConversations
            .Include(c => c.Turns).ThenInclude(t => t.TouchedExperts)
            .SingleAsync(c => c.Id == resolved.Id);

        conversation.UserId.Should().Be(alice);
        conversation.CreatedAt.Should().Be(Now);
        conversation.LastActiveAt.Should().Be(Now);
        conversation.Title.Should().Be("Who knows React?");

        var turn = conversation.Turns.Should().ContainSingle().Subject;
        turn.QuestionText.Should().Be("Who knows React?");
        turn.AnswerText.Should().Be("Ada Lovelace knows React.");
        turn.ModelId.Should().Be("gemini-3.5-flash-lite-002");
        turn.Grounded.Should().BeTrue();
        turn.State.Should().Be(RosterQaTurnState.Ok);
        turn.CreatedAt.Should().Be(Now);
        turn.TouchedExperts.Select(x => x.ExpertId).Should().Equal(ada);
    }

    [Fact]
    public async Task A_provider_that_named_no_model_stores_an_empty_model_id_rather_than_null()
    {
        var alice = await SeedUserAsync();
        var resolved = await ResolveAsync(alice, threadId: null);

        await AppendAsync(alice, resolved, "Who knows React?", Reply("Ada does.", modelId: null));

        await using var db = NewDb();
        (await db.RosterQaTurns.SingleAsync(t => t.ConversationId == resolved.Id)).ModelId.Should().BeEmpty();
    }

    [Fact]
    public async Task An_ungrounded_answer_is_stored_verbatim_and_flagged()
    {
        var alice = await SeedUserAsync();
        var resolved = await ResolveAsync(alice, threadId: null);
        const string answer = "Probably Ada.\n\n_Note: this answer could not be grounded in roster data._";

        await AppendAsync(alice, resolved, "Who knows React?", Reply(answer, grounded: false));

        await using var db = NewDb();
        var turn = await db.RosterQaTurns.SingleAsync(t => t.ConversationId == resolved.Id);
        turn.Grounded.Should().BeFalse("the flag comes off the run, never off the note in the text");
        turn.AnswerText.Should().Be(answer, "a replay must read exactly as the conversation did");
    }

    [Fact]
    public async Task Appending_moves_last_active_at_and_leaves_created_at_where_it_was()
    {
        var alice = await SeedUserAsync();
        var resolved = await ResolveAsync(alice, threadId: null);
        await AppendAsync(alice, resolved, "first", Reply("a1"));

        var later = Now.AddHours(3);
        var resumed = await ResolveAsync(alice, resolved.Id.ToString(), now: later);
        await AppendAsync(alice, resumed, "second", Reply("a2"), now: later);

        await using var db = NewDb();
        var conversation = await db.RosterQaConversations.SingleAsync(c => c.Id == resolved.Id);
        conversation.CreatedAt.Should().Be(Now);
        conversation.LastActiveAt.Should().Be(later);
        (await db.RosterQaTurns.CountAsync(t => t.ConversationId == resolved.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Only_the_first_turn_names_the_conversation()
    {
        var alice = await SeedUserAsync();
        var resolved = await ResolveAsync(alice, threadId: null);
        await AppendAsync(alice, resolved, "Who knows React?", Reply("Ada does."));

        var resumed = await ResolveAsync(alice, resolved.Id.ToString());
        await AppendAsync(alice, resumed, "And who knows COBOL?", Reply("Grace does."));

        await using var db = NewDb();
        (await db.RosterQaConversations.SingleAsync(c => c.Id == resolved.Id)).Title
            .Should().Be("Who knows React?", "there is no rename, and no model-written title");
    }

    [Theory]
    // Short enough to stand as it is.
    [InlineData("Who knows React?", "Who knows React?")]
    // 63 characters: cut at the last word boundary inside 60, with the ellipsis saying it was cut.
    [InlineData(
        "Which of our London engineers has led a payments platform rollout",
        "Which of our London engineers has led a payments platform…")]
    // A single 69-character word has no boundary to cut at, so the cut falls at 60.
    [InlineData(
        "Whoknowsreactandcobolandpythonandrustandgoandhaskellandeverythingelse",
        "Whoknowsreactandcobolandpythonandrustandgoandhaskellandevery…")]
    public async Task The_title_is_the_first_question_trimmed_at_a_word_boundary(string question, string expected)
    {
        var alice = await SeedUserAsync();
        var resolved = await ResolveAsync(alice, threadId: null);

        await AppendAsync(alice, resolved, question, Reply("answer"));

        await using var db = NewDb();
        var title = (await db.RosterQaConversations.SingleAsync(c => c.Id == resolved.Id)).Title;
        title.Should().Be(expected);
        title.Length.Should().BeLessThanOrEqualTo(80, "the column is 80 and the rule must never overrun it");
    }

    [Fact]
    public async Task Two_concurrent_appends_into_one_conversation_both_persist()
    {
        var alice = await SeedUserAsync();
        var resolved = await ResolveAsync(alice, threadId: null);
        await AppendAsync(alice, resolved, "first", Reply("a1"));

        // Two tabs, two connections, one conversation — each replays what exists when it starts
        // and appends its own turn. Turns are inserts, so neither can lose the other's.
        var left = await ResolveAsync(alice, resolved.Id.ToString());
        var right = await ResolveAsync(alice, resolved.Id.ToString());
        await Task.WhenAll(
            AppendAsync(alice, left, "left", Reply("from the left"), now: Now.AddSeconds(1)),
            AppendAsync(alice, right, "right", Reply("from the right"), now: Now.AddSeconds(2)));

        await using var db = NewDb();
        var questions = await db.RosterQaTurns
            .Where(t => t.ConversationId == resolved.Id)
            .Select(t => t.QuestionText)
            .ToListAsync();
        questions.Should().BeEquivalentTo(["first", "left", "right"]);
    }

    [Fact]
    public async Task A_conversation_deleted_mid_run_is_not_resurrected_by_the_turn_that_was_in_flight()
    {
        var alice = await SeedUserAsync();
        var resolved = await ResolveAsync(alice, threadId: null);
        await AppendAsync(alice, resolved, "first", Reply("a1"));

        var inFlight = await ResolveAsync(alice, resolved.Id.ToString());
        await using (var db = NewDb())
        {
            db.RosterQaConversations.Remove(await db.RosterQaConversations.SingleAsync(c => c.Id == resolved.Id));
            await db.SaveChangesAsync();
        }

        await AppendAsync(alice, inFlight, "second", Reply("a2"));

        await using var after = NewDb();
        (await after.RosterQaConversations.AnyAsync(c => c.Id == resolved.Id)).Should().BeFalse();
        (await after.RosterQaTurns.AnyAsync(t => t.ConversationId == resolved.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Another_owner_can_never_append_into_a_conversation_that_is_not_theirs()
    {
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        var hers = await SeedConversationAsync(alice);
        await SeedTurnAsync(hers, "q", "a", Now.AddMinutes(1));

        await AppendAsync(bob, new ResolvedConversation(hers, [], IsNew: false), "intrusion", Reply("nope"));

        await using var db = NewDb();
        (await db.RosterQaTurns.CountAsync(t => t.ConversationId == hers)).Should().Be(1);
    }

    // ---- plumbing -----------------------------------------------------------------------------

    private static AgentReply Reply(
        string text, string? modelId = "gemini-3.5-flash-lite", bool grounded = true, Guid[]? touched = null) =>
        new(text, 0, 0, 0, modelId, Grounded: grounded, TouchedExpertIds: touched ?? []);

    private async Task<ResolvedConversation> ResolveAsync(Guid userId, string? threadId, DateTimeOffset? now = null)
    {
        await using var db = NewDb();
        return await Store(db, now).ResolveAsync(userId, threadId);
    }

    private async Task AppendAsync(
        Guid userId, ResolvedConversation conversation, string question, AgentReply reply, DateTimeOffset? now = null)
    {
        await using var db = NewDb();
        await Store(db, now).AppendAsync(userId, conversation, question, reply);
    }

    private RosterQaConversationStore Store(AppDbContext db, DateTimeOffset? now) =>
        new(db, new FakeTimeProvider(now ?? Now));

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
        .Options);

    private async Task<Guid> SeedUserAsync()
    {
        await using var db = NewDb();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"asker-{Guid.NewGuid():N}@example.com",
            ControlWordHash = "hash",
            Role = UserRole.User,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

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

    private async Task<Guid> SeedConversationAsync(Guid ownerId, DateTimeOffset? lastActiveAt = null)
    {
        await using var db = NewDb();
        var conversation = new RosterQaConversation
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            CreatedAt = Now,
            LastActiveAt = lastActiveAt ?? Now,
            Title = "seeded",
        };
        db.RosterQaConversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation.Id;
    }

    private async Task SeedTurnAsync(
        Guid conversationId, string question, string answer, DateTimeOffset createdAt,
        params Guid[] touching)
        => await SeedTurnAsync(conversationId, question, answer, createdAt, RosterQaTurnState.Ok, touching);

    private async Task SeedTurnAsync(
        Guid conversationId, string question, string answer, DateTimeOffset createdAt,
        RosterQaTurnState state, params Guid[] touching)
    {
        await using var db = NewDb();
        var turn = new RosterQaTurn
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            QuestionText = question,
            AnswerText = answer,
            ModelId = "gemini-3.5-flash-lite",
            Grounded = true,
            State = state,
            CreatedAt = createdAt,
        };
        foreach (var expertId in touching)
        {
            turn.TouchedExperts.Add(new RosterQaTurnExpert { TurnId = turn.Id, ExpertId = expertId });
        }

        db.RosterQaTurns.Add(turn);
        await db.SaveChangesAsync();
    }
}
