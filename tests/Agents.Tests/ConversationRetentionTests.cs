using ExpertToJob.Agents.Compliance;
using ExpertToJob.Application.Abstractions;
using ExpertToJob.Domain.Entities;
using ExpertToJob.Domain.Enums;
using ExpertToJob.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// The six-month conversation sweep (EXP-35, <c>manuals/adr-roster-qa-conversation-history.md</c>
/// §6), against real Postgres because the deletion under test is a <em>cascade</em>: the turns and
/// their touched-Expert rows go because the database says they go, and the in-memory provider
/// cannot show that. <see cref="FakeTimeProvider"/> drives six calendar months past in a call.
///
/// <para>The arithmetic test is the one that earns its keep. <c>AddMonths(-6)</c> and
/// <c>TimeSpan.FromDays(180)</c> agree most of the year and disagree exactly where a promise gets
/// broken, so the edge is pinned with literal dates rather than derived from the code's own
/// expression.</para>
/// </summary>
public sealed class ConversationRetentionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ---- The period ---------------------------------------------------------------------------

    [Fact]
    public async Task Six_months_and_a_day_of_silence_deletes_the_conversation_with_everything_under_it()
    {
        var now = new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);
        var owner = await SeedOwnerAsync();
        var stale = await SeedConversationAsync(owner, lastActiveAt: now.AddMonths(-6).AddDays(-1));
        var fresh = await SeedConversationAsync(owner, lastActiveAt: now.AddMonths(-6).AddDays(1));

        (await SweepAsync(now)).Should().Be(1);

        await using var db = NewDb();
        (await db.RosterQaConversations.AnyAsync(c => c.Id == stale.ConversationId)).Should().BeFalse();
        (await db.RosterQaTurns.AnyAsync(t => t.ConversationId == stale.ConversationId))
            .Should().BeFalse("the turns go with it, by cascade");
        (await db.RosterQaTurnExperts.AnyAsync(x => x.TurnId == stale.TurnId))
            .Should().BeFalse("and so do the touched-Expert rows under those turns");

        (await db.RosterQaConversations.AnyAsync(c => c.Id == fresh.ConversationId))
            .Should().BeTrue("a day short of six months is still inside the promise");
        (await db.RosterQaTurns.CountAsync(t => t.ConversationId == fresh.ConversationId)).Should().Be(1);
        (await db.RosterQaTurnExperts.CountAsync(x => x.TurnId == fresh.TurnId)).Should().Be(1);
    }

    /// <summary>
    /// 31 August swept on 28 February. <c>AddMonths(-6)</c> lands on 28 August, so the conversation
    /// survives; 180 days lands on 1 September and would have deleted it. The dates are literals on
    /// purpose — a cutoff computed the way the code computes it would pass either way.
    /// </summary>
    [Fact]
    public async Task The_cutoff_is_six_calendar_months_not_a_hundred_and_eighty_days()
    {
        var owner = await SeedOwnerAsync();
        var lastAugust = await SeedConversationAsync(
            owner, lastActiveAt: new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));

        var sweptOn = new DateTimeOffset(2027, 2, 28, 12, 0, 0, TimeSpan.Zero);
        (await SweepAsync(sweptOn)).Should().Be(0);

        await using (var db = NewDb())
        {
            (await db.RosterQaConversations.AnyAsync(c => c.Id == lastAugust.ConversationId))
                .Should().BeTrue("28 February minus six calendar months is 28 August, which 31 August outlives");
        }

        // One day later the calendar agrees it is over: 1 March minus six months is 1 September.
        (await SweepAsync(sweptOn.AddDays(1))).Should().Be(1);

        await using var after = NewDb();
        (await after.RosterQaConversations.AnyAsync(c => c.Id == lastAugust.ConversationId)).Should().BeFalse();
    }

    [Fact]
    public async Task Another_owners_live_conversation_is_never_collateral()
    {
        var now = new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);
        var mine = await SeedOwnerAsync();
        var theirs = await SeedOwnerAsync();
        var stale = await SeedConversationAsync(mine, now.AddYears(-1));
        var live = await SeedConversationAsync(theirs, now.AddDays(-1));

        await SweepAsync(now);

        await using var db = NewDb();
        (await db.RosterQaConversations.AnyAsync(c => c.Id == stale.ConversationId)).Should().BeFalse();
        (await db.RosterQaConversations.AnyAsync(c => c.Id == live.ConversationId)).Should().BeTrue();
    }

    // ---- What it must never touch -------------------------------------------------------------

    /// <summary>
    /// The sweep deletes transcripts, never people. A stale conversation that touched an Expert
    /// takes its own rows and stops: the roster row, its lawful-basis history and its Retention
    /// Clock read exactly as they did before — agent activity never moves that clock, and this
    /// sweep is not the exception.
    /// </summary>
    [Fact]
    public async Task It_deletes_transcripts_and_leaves_the_expert_its_record_and_its_clock_alone()
    {
        var now = new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);
        var owner = await SeedOwnerAsync();
        var expertId = await SeedExpertAsync(
            lastActivityAt: new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
        var stale = await SeedConversationAsync(owner, now.AddYears(-1), touching: expertId);

        Expert before;
        ProcessingRecord recordBefore;
        await using (var db = NewDb())
        {
            before = await db.Experts.AsNoTracking().SingleAsync(e => e.Id == expertId);
            recordBefore = await db.ProcessingRecords.AsNoTracking().SingleAsync(p => p.ExpertId == expertId);
        }

        await SweepAsync(now);

        await using var after = NewDb();
        (await after.RosterQaConversations.AnyAsync(c => c.Id == stale.ConversationId)).Should().BeFalse();

        var expert = await after.Experts.AsNoTracking().SingleAsync(e => e.Id == expertId);
        expert.Should().BeEquivalentTo(before, o => o.Excluding(e => e.ProcessingRecords),
            "the Retention Clock is the person's, not the transcript's");

        var record = await after.ProcessingRecords.AsNoTracking().SingleAsync(p => p.ExpertId == expertId);
        record.Should().BeEquivalentTo(recordBefore, o => o.Excluding(p => p.Expert),
            "the basis history is append-only and this is not an append");
    }

    // ---- The worker around it -----------------------------------------------------------------

    [Fact]
    public async Task The_worker_sweeps_on_startup_and_says_how_many_it_deleted()
    {
        var now = new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);
        var owner = await SeedOwnerAsync();
        var stale = await SeedConversationAsync(owner, now.AddYears(-1));

        var logger = await RunWorkerAsync(
            new ConversationRetentionOptions { Enabled = true }, now,
            until: () => GoneAsync(stale.ConversationId));

        await using var db = NewDb();
        (await db.RosterQaConversations.AnyAsync(c => c.Id == stale.ConversationId)).Should().BeFalse();
        logger.Lines.Should().Contain(l => l.Contains("deleted 1 conversation"),
            "a background job that destroys data must never leave 'it went quiet' as the only evidence");
    }

    [Fact]
    public async Task A_disabled_worker_deletes_nothing()
    {
        var now = new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);
        var owner = await SeedOwnerAsync();
        var stale = await SeedConversationAsync(owner, now.AddYears(-1));

        await RunWorkerAsync(new ConversationRetentionOptions { Enabled = false }, now, until: null);

        await using var db = NewDb();
        (await db.RosterQaConversations.AnyAsync(c => c.Id == stale.ConversationId)).Should().BeTrue();
        (await db.RosterQaTurns.CountAsync(t => t.ConversationId == stale.ConversationId)).Should().Be(1);
    }

    [Fact]
    public void The_sweep_is_on_by_default_because_it_deletes_transcripts_never_people()
        => new ConversationRetentionOptions().Enabled.Should().BeTrue();

    // ---- Plumbing -----------------------------------------------------------------------------

    private async Task<int> SweepAsync(DateTimeOffset now)
    {
        await using var db = NewDb();
        return await new ConversationRetentionSweep(db, new FakeTimeProvider(now)).RunOnceAsync();
    }

    /// <summary>
    /// Starts the real <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> and waits for
    /// the pass it makes before its first tick. A disabled worker finishes outright; an enabled one
    /// stays in its daily timer, so the wait is for the deletion rather than for the task.
    /// </summary>
    private async Task<CapturingLogger<ConversationRetentionWorker>> RunWorkerAsync(
        ConversationRetentionOptions options, DateTimeOffset now, Func<Task<bool>>? until)
    {
        var clock = new FakeTimeProvider(now);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddDbContext<AppDbContext>(o =>
            o.UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ConversationRetentionSweep>();
        await using var provider = services.BuildServiceProvider();

        var logger = new CapturingLogger<ConversationRetentionWorker>();
        var worker = new ConversationRetentionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), options, clock, logger);

        await worker.StartAsync(CancellationToken.None);
        if (until is null)
        {
            await worker.ExecuteTask!;
        }
        else
        {
            await WaitForAsync(until, logger);
        }

        await worker.StopAsync(CancellationToken.None);
        return logger;
    }

    private async Task<bool> GoneAsync(Guid conversationId)
    {
        await using var db = NewDb();
        return !await db.RosterQaConversations.AnyAsync(c => c.Id == conversationId);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, CapturingLogger<ConversationRetentionWorker> logger)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException(
            "The worker's first pass did not finish within 30 seconds. Logged: "
            + string.Join(" | ", logger.Lines));
    }

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
        .Options);

    private async Task<Guid> SeedOwnerAsync()
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

    private async Task<Guid> SeedExpertAsync(DateTimeOffset lastActivityAt)
    {
        await using var db = NewDb();
        var expert = new Expert
        {
            Id = Guid.NewGuid(),
            FirstName = "Ada",
            LastName = "Lovelace",
            Title = "Engineer",
            Email = $"ada-{Guid.NewGuid():N}@example.com",
            LastActivityAt = lastActivityAt,
        };
        expert.ProcessingRecords.Add(new ProcessingRecord
        {
            Id = Guid.NewGuid(),
            ExpertId = expert.Id,
            Sequence = 1,
            Origin = ProcessingOrigin.StaffCreated,
            Basis = ProcessingRecord.BasisFor(ProcessingOrigin.StaffCreated),
            Reason = "A Service Manager entered this record.",
            RecordedAt = new DateTimeOffset(2026, 1, 4, 10, 0, 0, TimeSpan.Zero),
        });
        db.Experts.Add(expert);
        await db.SaveChangesAsync();
        return expert.Id;
    }

    private async Task<(Guid ConversationId, Guid TurnId)> SeedConversationAsync(
        Guid ownerId, DateTimeOffset lastActiveAt, Guid? touching = null)
    {
        await using var db = NewDb();
        var conversation = new RosterQaConversation
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            CreatedAt = lastActiveAt.AddMinutes(-5),
            LastActiveAt = lastActiveAt,
            Title = "Who is free in May?",
        };
        var turn = new RosterQaTurn
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            QuestionText = "Who is free in May?",
            AnswerText = "Ada Lovelace is.",
            ModelId = "gemini-3.5-flash-lite",
            Grounded = true,
            CreatedAt = lastActiveAt,
        };
        turn.TouchedExperts.Add(new RosterQaTurnExpert
        {
            TurnId = turn.Id,
            ExpertId = touching ?? Guid.NewGuid(),
        });

        conversation.Turns.Add(turn);
        db.RosterQaConversations.Add(conversation);
        await db.SaveChangesAsync();

        return (conversation.Id, turn.Id);
    }

    /// <summary>Keeps the rendered log lines, so "it logs how many it deleted" is an assertion
    /// rather than a claim.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get { lock (_lines) { return _lines.ToArray(); } }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_lines)
            {
                _lines.Add(formatter(state, exception) + (exception is null ? "" : $" [{exception}]"));
            }
        }
    }
}
