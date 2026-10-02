using System.Reflection;
using ExpertToJob.Agents.RosterScan;
using FluentAssertions;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// How often <c>RosterScanWorker</c> sweeps the store for jobs that became due — paused jobs whose
/// resume time arrived, queued jobs nobody picked up, running jobs orphaned by a restart.
///
/// <para>A constant since EXP-106. It was bindable, and no settings file, AppHost parameter or test
/// ever set it; the quota knobs beside it (<c>RequestsPerMinute</c>, <c>RequestsPerDay</c>) stay
/// configurable on purpose, because an operator may genuinely need to move those without a deploy.
/// This one is a liveness detail of the worker, not an operator's decision.</para>
/// </summary>
public class RosterScanSweepIntervalTests
{
    /// <summary>The literal, not a read of the constant it came from — that would pass on any
    /// value. Changing the sweep cadence takes an edit here too.</summary>
    [Fact]
    public void The_sweep_interval_is_the_shipped_number()
        => RosterScanOptions.ResumeSweepSeconds.Should().Be(30);

    /// <summary>And there is no property left for a <c>RosterScan:ResumeSweepSeconds</c> key to
    /// bind to. The compiler cannot see this one coming back: binding answers a key it does not
    /// know with silence, so a re-added property would simply be a second place the number lives.
    /// </summary>
    [Fact]
    public void The_sweep_interval_is_not_a_bindable_property()
        => typeof(RosterScanOptions)
            .GetProperty("ResumeSweepSeconds", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull("the sweep cadence is a constant, not a RosterScan configuration key");
}
