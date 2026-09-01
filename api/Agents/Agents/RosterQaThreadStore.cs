using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace ExpertToJob.Agents.Agents;

/// <summary>What a thread lookup produced: the id to answer with (fresh when the requested one
/// was unknown or expired — the client detects context loss by the id changing) and the bounded
/// history to replay into the model.</summary>
public sealed record RosterQaThread(string ThreadId, IReadOnlyList<ChatMessage> History);

/// <summary>
/// In-memory conversation store for Roster Q&A (P1T-93, per the P1T-82 decisions): per-user
/// threads with a 30-minute sliding TTL and an LRU cap of 20 live threads per user. History keeps
/// only the final question/answer text of the last 10 turns — tool-call intermediates are not
/// replayed — so prompts stay bounded without a summarizer. Lost on restart by design (showcase
/// scope; the durable-runtime alternative was rejected in the MAF research).
/// </summary>
public sealed class RosterQaThreadStore
{
    private const int MaxTurns = 10;
    private const int MaxThreadsPerUser = 20;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private sealed class Entry
    {
        public required string UserKey { get; init; }
        public List<ChatMessage> History { get; } = [];
        public DateTimeOffset LastUsed { get; set; }
    }

    private readonly ConcurrentDictionary<string, Entry> _threads = new();
    private readonly TimeProvider _time;

    public RosterQaThreadStore(TimeProvider time) => _time = time;

    /// <summary>Resolves the caller's thread: the existing one when the id is known, fresh
    /// otherwise (missing, expired, or another user's). Touching it slides the TTL.</summary>
    public RosterQaThread Resolve(Guid? userId, string? threadId)
    {
        var userKey = UserKey(userId);
        var now = _time.GetUtcNow();
        Sweep(now);

        if (threadId is not null
            && _threads.TryGetValue(threadId, out var entry)
            && entry.UserKey == userKey
            && now - entry.LastUsed <= Ttl)
        {
            lock (entry)
            {
                entry.LastUsed = now;
                return new RosterQaThread(threadId, entry.History.ToList());
            }
        }

        var freshId = Guid.NewGuid().ToString("N");
        _threads[freshId] = new Entry { UserKey = userKey, LastUsed = now };
        EvictOverCap(userKey);
        return new RosterQaThread(freshId, []);
    }

    /// <summary>Records one finished turn and trims the thread to the last N turns.</summary>
    public void Append(Guid? userId, string threadId, string question, string answer)
    {
        if (!_threads.TryGetValue(threadId, out var entry) || entry.UserKey != UserKey(userId))
        {
            return; // evicted mid-run — the next Resolve starts fresh, nothing to record
        }

        lock (entry)
        {
            entry.History.Add(new ChatMessage(ChatRole.User, question));
            entry.History.Add(new ChatMessage(ChatRole.Assistant, answer));
            var excess = entry.History.Count - MaxTurns * 2;
            if (excess > 0)
            {
                entry.History.RemoveRange(0, excess);
            }

            entry.LastUsed = _time.GetUtcNow();
        }
    }

    private static string UserKey(Guid? userId) => userId?.ToString() ?? "anonymous";

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (id, entry) in _threads)
        {
            if (now - entry.LastUsed > Ttl)
            {
                _threads.TryRemove(id, out _);
            }
        }
    }

    private void EvictOverCap(string userKey)
    {
        var mine = _threads.Where(kv => kv.Value.UserKey == userKey).ToList();
        if (mine.Count <= MaxThreadsPerUser)
        {
            return;
        }

        foreach (var (id, _) in mine
                     .OrderBy(kv => kv.Value.LastUsed)
                     .Take(mine.Count - MaxThreadsPerUser))
        {
            _threads.TryRemove(id, out _);
        }
    }
}
