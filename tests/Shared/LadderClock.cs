using Microsoft.Extensions.Time.Testing;

namespace ExpertToJob.Tests.Shared;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that reads a retry ladder without spending it: every wait asked
/// of the clock is recorded in <see cref="Waits"/> and then granted at once, so a test can assert
/// the exact backoff sequence (5s then 10s, 2s then 4s, …) in milliseconds rather than minutes.
///
/// <para>Shared by Agents.Tests and Mcp.Tests as a linked file — the three 429 ladders it measures
/// live in two different projects (EXP-87), and a second copy is a second thing to get wrong.</para>
/// </summary>
internal sealed class LadderClock : FakeTimeProvider
{
    private readonly List<TimeSpan> _waits = [];

    /// <summary>The waits asked of this clock, in order.</summary>
    public IReadOnlyList<TimeSpan> Waits
    {
        get { lock (_waits) { return [.. _waits]; } }
    }

    public override ITimer CreateTimer(
        TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        lock (_waits)
        {
            _waits.Add(dueTime);
        }

        // The timer is registered by now, so advancing fires it. Off this call stack, because the
        // fake clock must not be re-entered from inside its own CreateTimer.
        ThreadPool.QueueUserWorkItem(_ => Advance(dueTime));
        return timer;
    }
}
