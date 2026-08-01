using CvManager.Agents.Agents;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace CvManager.Agents.Tests;

/// <summary>The P1T-82 thread-lifecycle decisions: sliding 30-minute TTL, LRU cap of 20 threads
/// per user, silent fresh thread on unknown/expired/foreign ids, history bounded to 10 turns.</summary>
public class RosterQaThreadStoreTests
{
    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void A_missing_thread_id_starts_a_fresh_empty_thread()
    {
        var store = new RosterQaThreadStore(new FakeTime());
        var thread = store.Resolve(Alice, null);
        thread.ThreadId.Should().NotBeNullOrEmpty();
        thread.History.Should().BeEmpty();
    }

    [Fact]
    public void A_known_thread_returns_its_history_in_order()
    {
        var store = new RosterQaThreadStore(new FakeTime());
        var thread = store.Resolve(Alice, null);
        store.Append(Alice, thread.ThreadId, "who knows React?", "Ada Lovelace does.");

        var resumed = store.Resolve(Alice, thread.ThreadId);

        resumed.ThreadId.Should().Be(thread.ThreadId);
        resumed.History.Should().HaveCount(2);
        resumed.History[0].Text.Should().Be("who knows React?");
        resumed.History[1].Role.Should().Be(ChatRole.Assistant);
    }

    [Fact]
    public void An_expired_thread_silently_becomes_a_fresh_one_with_a_new_id()
    {
        var time = new FakeTime();
        var store = new RosterQaThreadStore(time);
        var thread = store.Resolve(Alice, null);
        store.Append(Alice, thread.ThreadId, "q", "a");

        time.Now += TimeSpan.FromMinutes(31);
        var resumed = store.Resolve(Alice, thread.ThreadId);

        resumed.ThreadId.Should().NotBe(thread.ThreadId, "the id change is how the client detects context loss");
        resumed.History.Should().BeEmpty();
    }

    [Fact]
    public void Activity_slides_the_ttl()
    {
        var time = new FakeTime();
        var store = new RosterQaThreadStore(time);
        var thread = store.Resolve(Alice, null);

        time.Now += TimeSpan.FromMinutes(20);
        store.Resolve(Alice, thread.ThreadId); // touch
        time.Now += TimeSpan.FromMinutes(20);

        store.Resolve(Alice, thread.ThreadId).ThreadId.Should().Be(thread.ThreadId,
            "40 minutes with a touch at 20 stays inside the sliding window");
    }

    [Fact]
    public void Another_users_thread_id_is_never_resumed()
    {
        var store = new RosterQaThreadStore(new FakeTime());
        var thread = store.Resolve(Alice, null);
        store.Append(Alice, thread.ThreadId, "secret question", "secret answer");

        var resumed = store.Resolve(Bob, thread.ThreadId);

        resumed.ThreadId.Should().NotBe(thread.ThreadId);
        resumed.History.Should().BeEmpty();
    }

    [Fact]
    public void History_is_trimmed_to_the_last_ten_turns()
    {
        var store = new RosterQaThreadStore(new FakeTime());
        var thread = store.Resolve(Alice, null);
        for (var i = 1; i <= 12; i++)
        {
            store.Append(Alice, thread.ThreadId, $"q{i}", $"a{i}");
        }

        var resumed = store.Resolve(Alice, thread.ThreadId);

        resumed.History.Should().HaveCount(20);
        resumed.History[0].Text.Should().Be("q3", "the two oldest turns fall off");
        resumed.History[^1].Text.Should().Be("a12");
    }

    [Fact]
    public void The_oldest_thread_is_evicted_beyond_twenty_per_user()
    {
        var time = new FakeTime();
        var store = new RosterQaThreadStore(time);

        var first = store.Resolve(Alice, null);
        var bobs = store.Resolve(Bob, null);
        for (var i = 0; i < 20; i++)
        {
            time.Now += TimeSpan.FromSeconds(1);
            store.Resolve(Alice, null); // 20 more Alice threads → 21 total → first evicted
        }

        store.Resolve(Alice, first.ThreadId).ThreadId.Should().NotBe(first.ThreadId);
        store.Resolve(Bob, bobs.ThreadId).ThreadId.Should().Be(bobs.ThreadId,
            "the cap is per user — Bob's thread is untouched");
    }
}
