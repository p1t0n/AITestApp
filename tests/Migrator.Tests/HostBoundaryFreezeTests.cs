using FluentAssertions;

namespace ExpertToJob.Migrator.Tests;

/// <summary>
/// Which process owns the schema (P1T-215). The Web host used to apply migrations and seed on a
/// Development start, which meant every other process — the MCP server most of all — could only run
/// against a database the API had already visited. That coupling is what this slice removes, and
/// this is the assertion that keeps it removed.
/// </summary>
/// <remarks>
/// Reading the two <c>Program.cs</c> files as data is the same move
/// <c>tests/ServiceDefaults.Tests/HostTelemetryFreezeTests.cs</c> makes, and for the same reason: a
/// test that asked the running host what it did would pass whatever the host said, including the
/// thing this slice deleted. Re-adding a <c>MigrateAsync</c> to the API is a silent regression —
/// the suite stays green, the API still works, and the MCP server quietly needs it again.
/// </remarks>
public class HostBoundaryFreezeTests
{
    [Theory]
    [InlineData("MigrateAsync")]
    [InlineData("DbInitializer.SeedAsync")]
    [InlineData("Seed:DemoRoster")]
    [InlineData("DemoRosterSeeder")]
    public void The_web_host_no_longer_owns_the_schema_or_the_demo_roster(string forbidden)
    {
        // tools/SeedDemoRoster is the only demo-roster path now; a boot-time flag that was set
        // nowhere is not a second one.
        ReadProgram("web").Should().NotContain(forbidden);
    }

    [Fact]
    public void The_web_host_keeps_the_service_manager_bootstrap()
    {
        // Not part of this slice: it runs in every environment, including production, because
        // signup only makes Experts and a fresh database would otherwise have nobody who can reach
        // the roster (P1T-181).
        ReadProgram("web").Should().Contain("ServiceManagerBootstrapper.EnsureAsync");
    }

    [Fact]
    public void The_migrator_takes_the_shared_spine()
    {
        ReadProgram("migrator").Should().Contain("AddServiceDefaults()");
    }

    [Fact]
    public void The_migrator_maps_no_endpoints()
    {
        // It is a one-shot console, not a host: there is nothing for /health to describe, and a
        // process that exits cannot be probed.
        ReadProgram("migrator").Should().NotContain("MapDefaultEndpoints");
    }

    /// <summary>
    /// The host's startup file with its whole-line comments removed, so the freeze reads what the
    /// process does rather than what it says about itself. Both files name, in prose, the thing
    /// they deliberately do not call — which is worth keeping, and is not a call.
    /// </summary>
    private static string ReadProgram(string host)
    {
        var source = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "hosts", $"{host}-Program.cs.txt"));

        return string.Join(
            Environment.NewLine,
            source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }
}
