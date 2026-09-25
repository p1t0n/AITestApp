using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExpertToJob.Agents.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ExpertToJob.Agents.Tests;

/// <summary>
/// GET /agents/models (EXP-31): which model answers on which dock surface, reported before anyone
/// asks. Run against the real host — the point is the composed answer, and the seam's catalog is
/// what composes it, so faking either half would test the fake.
///
/// <para>The last two tests are the anti-drift half, and they are the reason the surface map is
/// allowed to be a hand-written table at all: they read <c>api/Agents</c>'s own source for every
/// <c>ResolveAgentChatClient</c> call site and hold the map against it in both directions. A new
/// agent that no surface claims is red here rather than a dock caption that quietly stops being
/// true.</para>
/// </summary>
public class AgentModelsEndpointTests
{
    /// <summary>The nine surface ids, as the SPA's <c>Surface</c> union spells them. Written out
    /// rather than read off <see cref="AgentSurfaces.AgentKeys"/>, because a test that derives its
    /// expectation from the thing under test asserts nothing about the ids.</summary>
    private static readonly string[] DockSurfaces =
    [
        "roster", "cv-tailoring", "match", "interview-kit", "shortlist",
        "staffing", "roster-scan", "bench", "ingestion",
    ];

    private const string TestEndpoint = "https://models.test.invalid/v1";
    private const string TestApiKey = "test-key-not-a-real-one";

    /// <summary>
    /// The real host, with the whole <c>Ai:*</c> block supplied as host settings.
    ///
    /// <para><c>UseSetting</c> rather than <c>ConfigureAppConfiguration</c>, and the difference is
    /// load-bearing under minimal hosting: <c>Program.cs</c> reads <c>builder.Configuration</c> at
    /// <c>AddChatProvider</c> time, and a source added by a deferred web-host callback arrives
    /// after the seam has already decided — the override binds to nothing and the test passes
    /// against the shipped defaults. Measured rather than assumed: the first draft of this file
    /// did exactly that.</para>
    /// </summary>
    private static WebApplicationFactory<Program> Host(params (string Agent, string Model)[] overrides) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Ai:Chat:Provider", nameof(ChatProvider.Gemini));
            b.UseSetting("Ai:Gemini:Endpoint", TestEndpoint);
            b.UseSetting("Ai:Gemini:Model", "model-default");
            b.UseSetting("Ai:Gemini:ApiKey", TestApiKey);
            foreach (var (agent, model) in overrides)
            {
                b.UseSetting($"Ai:Gemini:Agents:{agent}", model);
            }

            b.ConfigureServices(s => s.AddInMemoryAppDb("agent-models"));
        });

    private static async Task<JsonElement> GetModels(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/agents/models");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static string[] ModelsFor(JsonElement body, string surface) =>
        body.GetProperty("surfaces").GetProperty(surface).EnumerateArray()
            .Select(m => m.GetString()!).ToArray();

    [Fact]
    public async Task Requires_a_session_like_every_other_agents_endpoint()
    {
        using var factory = Host();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/agents/models");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Names_the_configured_provider()
    {
        using var factory = Host();

        var body = await GetModels(factory);

        body.GetProperty("provider").GetString().Should().Be(nameof(ChatProvider.Gemini));
    }

    [Fact]
    public async Task With_no_overrides_every_surface_lists_the_default()
    {
        using var factory = Host();

        var body = await GetModels(factory);

        body.GetProperty("surfaces").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(DockSurfaces, "one entry per dock surface, no more and no fewer");
        foreach (var surface in DockSurfaces)
        {
            ModelsFor(body, surface).Should().Equal(["model-default"], $"'{surface}' overrides nothing");
        }
    }

    [Fact]
    public async Task An_override_changes_only_the_surfaces_that_use_that_agent()
    {
        // bench-report is the narrowest key there is: exactly one surface runs it.
        using var factory = Host(("bench-report", "model-bench"));

        var body = await GetModels(factory);

        ModelsFor(body, "bench").Should().Equal(["model-bench"]);
        foreach (var untouched in DockSurfaces.Where(s => s != "bench"))
        {
            ModelsFor(body, untouched).Should().Equal(["model-default"],
                $"nothing behind '{untouched}' resolves a chat client by 'bench-report'");
        }
    }

    [Fact]
    public async Task An_override_reaches_every_surface_that_runs_that_agent()
    {
        // shortlist runs under Shortlist and inside the Staffing pipeline, and nowhere else.
        using var factory = Host(("shortlist", "model-shortlist"));

        var body = await GetModels(factory);

        ModelsFor(body, "shortlist").Should().Equal(["model-default", "model-shortlist"]);
        ModelsFor(body, "staffing").Should().Equal(["model-default", "model-shortlist"]);
        ModelsFor(body, "roster").Should().Equal(["model-default"]);
    }

    [Fact]
    public async Task A_surface_backed_by_several_agents_lists_each_distinct_model_once_sorted()
    {
        using var factory = Host(
            ("staffing", "model-zulu"),
            ("match", "model-alpha"),
            // Same model as the default: the surface must not report it twice.
            ("shortlist", "model-default"));

        var body = await GetModels(factory);

        // staffing runs jd-extraction (default), shortlist (default), match, staffing.
        ModelsFor(body, "staffing").Should().Equal(["model-alpha", "model-default", "model-zulu"]);
    }

    [Fact]
    public async Task The_response_carries_no_credential_and_no_endpoint()
    {
        using var factory = Host();
        using var client = factory.CreateAuthenticatedClient();

        var raw = await (await client.GetAsync("/agents/models")).Content.ReadAsStringAsync();

        raw.Should().NotContain(TestApiKey, "a model catalog served to a browser must not carry the key");
        raw.Should().NotContain("models.test.invalid", "nor the provider endpoint");

        // And nothing beyond the two fields the contract names — a field added later is a field
        // nobody re-read this rule before adding.
        JsonDocument.Parse(raw).RootElement.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["provider", "surfaces"]);
    }

    // ---- the anti-drift half: the map, held against the source it describes ----

    /// <summary>Matches a keyed-client lookup and captures how the agent key was spelled — a string
    /// literal, or a named constant (<c>JdRequirementExtractor.AgentName</c>), which the resolver
    /// below reads by reflection rather than by re-typing its value here.</summary>
    private static readonly Regex Lookup = new(
        @"ResolveAgentChatClient\(\s*(?:""(?<literal>[^""]+)""|(?<constant>[A-Za-z_][\w.]*))\s*\)",
        RegexOptions.Compiled);

    [Fact]
    public void Every_agent_key_the_source_resolves_a_client_by_appears_under_some_surface()
    {
        var used = AgentKeysUsedInSource();

        // The sweep has to have found something recognisable, or the assertion below is vacuous.
        used.Should().Contain("roster-qa").And.Contain("staffing").And.Contain("jd-extraction");
        used.Should().HaveCountGreaterThan(8, "api/Agents wires one chat client per agent");

        var claimed = AgentSurfaces.AgentKeys.Values.SelectMany(keys => keys).ToHashSet(StringComparer.Ordinal);
        var orphans = used.Where(key => !claimed.Contains(key)).OrderBy(k => k, StringComparer.Ordinal).ToList();

        orphans.Should().BeEmpty(
            "an agent key that no dock surface claims is a surface whose reported model is silently "
            + "incomplete — add it to AgentSurfaces.AgentKeys. Found: " + string.Join(", ", orphans));
    }

    [Fact]
    public void Every_agent_key_the_map_claims_is_one_the_source_actually_resolves()
    {
        var used = AgentKeysUsedInSource();

        var invented = AgentSurfaces.AgentKeys.Values.SelectMany(keys => keys)
            .Distinct(StringComparer.Ordinal)
            .Where(key => !used.Contains(key))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        invented.Should().BeEmpty(
            "a surface naming an agent key nothing resolves a client by would report a model for an "
            + "agent that does not run — an override on it would appear to take effect and would not. "
            + "Found: " + string.Join(", ", invented));
    }

    /// <summary>Every agent key <c>api/Agents</c> asks for a chat client by, read out of its own
    /// source. Tests are deliberately outside the sweep: they resolve keys nobody ships
    /// (<c>an-agent-nobody-configured</c>), and a fixture is not a wiring.</summary>
    private static HashSet<string> AgentKeysUsedInSource()
    {
        var agentsDir = Path.Combine(RepoRoot(), "api", "Agents");
        Directory.Exists(agentsDir).Should().BeTrue("the sweep reads api/Agents from the repo root");

        var separator = Path.DirectorySeparatorChar;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(agentsDir, "*.cs", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
                                 && !p.Contains($"{separator}obj{separator}", StringComparison.Ordinal)))
        {
            foreach (Match match in Lookup.Matches(File.ReadAllText(file)))
            {
                keys.Add(match.Groups["literal"].Success
                    ? match.Groups["literal"].Value
                    : ConstantValue(match.Groups["constant"].Value));
            }
        }

        return keys;
    }

    /// <summary>Resolves a <c>Type.Member</c> reference to the constant's value, against the loaded
    /// Agents assembly. Reflection rather than a lookup table on purpose: a table would be a second
    /// copy of the very value this file exists to avoid copying.</summary>
    private static string ConstantValue(string reference)
    {
        var split = reference.LastIndexOf('.');
        split.Should().BeGreaterThan(0, $"'{reference}' is not a Type.Member reference the sweep can resolve");

        var typeName = reference[..split];
        var memberName = reference[(split + 1)..];
        var type = typeof(ChatModelCatalog).Assembly.GetTypes()
            .FirstOrDefault(t => t.Name == typeName)
            ?? throw new InvalidOperationException(
                $"ResolveAgentChatClient is called with '{reference}', but no type named '{typeName}' "
                + "is in the Agents assembly — the sweep cannot read the key it names.");

        var value = type.GetField(memberName)?.GetValue(null) as string
            ?? throw new InvalidOperationException(
                $"'{reference}' is not a public static string on {type.FullName}.");
        return value;
    }

    /// <summary>Walks up from the test binary to the solution file — the same trick
    /// <c>ConfigKeyMigrationTests</c> uses, and for the same reason.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ExpertToJob.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not find ExpertToJob.slnx above the test binary; the agent-key sweep cannot run.");
    }
}
