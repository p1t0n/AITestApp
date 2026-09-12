using System.Text.RegularExpressions;
using FluentAssertions;

namespace ExpertToJob.ServiceDefaults.Tests;

/// <summary>
/// A freeze on what each host subscribes (P1T-214). The shared <c>ServiceDefaults</c> project owns
/// the exporter and the generic instrumentation; the per-host source and meter lists stay next to
/// the code that emits them, and this is the assertion that says so out loud.
/// </summary>
/// <remarks>
/// <para>Reading the three <c>Program.cs</c> files as data is deliberate, and it is the same move
/// <c>web/src/frozenHooks.test.ts</c> makes on the SPA: a test that asked the host for its list
/// would mirror production and pass whatever production said, including nothing. These literals are
/// a second copy on purpose. Dropping one from a host is a silent telemetry regression — no build
/// error, no failing request, just an empty trace — and adding one without a thought is how a
/// pipeline grows a source nobody chose.</para>
/// <para>If a list genuinely changes, change it here too, in the same commit, and say why.</para>
/// </remarks>
public class HostTelemetryFreezeTests
{
    private static readonly string[] WebSources = ["Npgsql"];
    private static readonly string[] WebMeters = ["Npgsql"];

    private static readonly string[] McpSources =
    [
        "Experimental.ModelContextProtocol",
        "Experimental.Microsoft.Extensions.AI",
        "System.Net.Http",
        "Npgsql",
    ];

    private static readonly string[] McpMeters =
    [
        "Experimental.ModelContextProtocol",
        "Experimental.Microsoft.Extensions.AI",
        "System.Net.Http",
        "Npgsql",
    ];

    private static readonly string[] AgentsSources =
    [
        "Experimental.Microsoft.Extensions.AI",
        "Experimental.Microsoft.Agents.AI",
        "Microsoft.Agents.AI.Workflows",
        "Experimental.ModelContextProtocol",
        "ExpertToJob.Agents.RosterScan",
        "System.Net.Http",
        "Npgsql",
    ];

    private static readonly string[] AgentsMeters =
    [
        "Experimental.Microsoft.Extensions.AI",
        "Experimental.ModelContextProtocol",
        "System.Net.Http",
        "Npgsql",
    ];

    public static TheoryData<string, string[], string[]> Hosts => new()
    {
        { "web", WebSources, WebMeters },
        { "mcp", McpSources, McpMeters },
        { "agents", AgentsSources, AgentsMeters },
    };

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Each_host_subscribes_exactly_its_frozen_sources_and_meters(
        string host,
        string[] expectedSources,
        string[] expectedMeters)
    {
        var program = ReadProgram(host);

        ArgumentLiterals(program, "AddSource").Should().Equal(expectedSources);
        ArgumentLiterals(program, "AddMeter").Should().Equal(expectedMeters);
    }

    [Theory]
    [InlineData("web", "experttojob-web")]
    [InlineData("mcp", "experttojob-mcp")]
    [InlineData("agents", "experttojob-agents")]
    public void Each_host_names_its_own_otel_resource(string host, string serviceName)
    {
        // The AppHost resource names match these, so telemetry and the resource list never disagree.
        ReadProgram(host).Should().Contain($"r.AddService(\"{serviceName}\")");
    }

    [Theory]
    [InlineData("web")]
    [InlineData("mcp")]
    [InlineData("agents")]
    public void No_host_registers_its_own_otlp_exporter(string host)
    {
        // The exporter is ServiceDefaults' and is conditional on OTEL_EXPORTER_OTLP_ENDPOINT.
        // A host calling AddOtlpExporter() here would export unconditionally again and, with
        // UseOtlpExporter() already applied, double-register.
        ReadProgram(host).Should().NotContain("AddOtlpExporter");
    }

    [Theory]
    [InlineData("web")]
    [InlineData("mcp")]
    [InlineData("agents")]
    public void No_host_wires_service_discovery(string host)
    {
        // Nothing in this stack can consume a "https+http://servicename" URL — the MCP client is a
        // hand-built HttpClient and the Keycloak token provider concatenates strings. A file
        // advertising the capability is how the next person comes to believe one resolves.
        ReadProgram(host).Should().NotContain("AddServiceDiscovery");
    }

    private static string ReadProgram(string host) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hosts", $"{host}-Program.cs.txt"));

    /// <summary>
    /// The string literals passed to one call of <paramref name="method"/>, in source order.
    /// Balances parentheses rather than regexing the whole call, so a trailing comment on a literal
    /// (both hosts have several) does not end the match early.
    /// </summary>
    private static IReadOnlyList<string> ArgumentLiterals(string program, string method)
    {
        var call = $".{method}(";
        var open = program.IndexOf(call, StringComparison.Ordinal);
        open.Should().BeGreaterThanOrEqualTo(0, "{0} is the subscription under freeze", method);

        program.IndexOf(call, open + 1, StringComparison.Ordinal)
            .Should().Be(-1, "a second {0} call would split the list this freeze reads", method);

        var cursor = open + call.Length;
        var depth = 1;

        while (depth > 0)
        {
            cursor.Should().BeLessThan(program.Length, "the {0} call should be closed", method);
            depth += program[cursor] switch { '(' => 1, ')' => -1, _ => 0 };
            cursor++;
        }

        var arguments = program[(open + call.Length)..(cursor - 1)];

        return Regex.Matches(arguments, "\"([^\"]*)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }
}
