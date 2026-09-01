using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// The provisioning half of Tool Grants (P1T-149): each agent's grants are client scopes on its
/// own Keycloak client, so the narrowing rides on the identity rather than on client config.
///
/// <para>That creates a third copy of one fact, and the copies are what this holds together.
/// <c>CostFloors.AgentToolAllowlists</c> is the declaration; <c>Agents.Tests</c> asserts the
/// shipped <c>appsettings.json</c> matches it; this asserts the shipped realm does too. Chained,
/// the token's grants, the client's filter and the measured Baseline Prompt Size are provably the
/// same set — and the first edit that breaks that is a red test rather than an agent that quietly
/// loses a tool at runtime.</para>
///
/// <para>Deterministic: the realm export is JSON on disk (copied beside the test binary for the
/// Keycloak e2e), so nothing here needs Docker or a running authorization server.</para>
/// </summary>
public class KeycloakToolGrantTests
{
    private const string GrantPrefix = "mcp:tool:";

    private static readonly JsonDocument Realm = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "realm-export.json")));

    private static IEnumerable<JsonElement> Clients => Realm.RootElement.GetProperty("clients").EnumerateArray();

    private static string ClientId(JsonElement client) => client.GetProperty("clientId").GetString()!;

    private static IReadOnlyList<string> DefaultScopes(JsonElement client) =>
        client.GetProperty("defaultClientScopes").EnumerateArray().Select(s => s.GetString()!).ToList();

    /// <summary>The tools a client's default scopes grant it, with the prefix stripped.</summary>
    private static IReadOnlyList<string> GrantedTools(JsonElement client) =>
        DefaultScopes(client)
            .Where(s => s.StartsWith(GrantPrefix, StringComparison.Ordinal))
            .Select(s => s[GrantPrefix.Length..])
            .ToList();

    private static JsonElement Client(string clientId) =>
        Clients.Single(c => ClientId(c) == clientId);

    public static TheoryData<string> AgentKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in ExpertToJob.CostFloors.CostFloors.AgentToolAllowlists.Keys.Order())
        {
            data.Add(key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AgentKeys))]
    public void Each_agent_client_is_granted_exactly_its_declared_tool_set(string agentKey)
    {
        var client = Client($"agent-{agentKey}");

        GrantedTools(client).Should().BeEquivalentTo(
            ExpertToJob.CostFloors.CostFloors.AgentToolAllowlists[agentKey],
            $"agent-{agentKey}'s token is what the MCP server narrows on — if the realm and the "
            + "declared allowlist disagree, the agent runs on a surface nothing measured");
    }

    [Theory]
    [MemberData(nameof(AgentKeys))]
    public void No_agent_client_is_granted_a_tool_its_capability_scope_cannot_carry(string agentKey)
    {
        var client = Client($"agent-{agentKey}");
        var carried = DefaultScopes(client).Contains(McpScopes.Write)
            ? ExpertToJob.CostFloors.CostFloors.WriteScopeTools
            : ExpertToJob.CostFloors.CostFloors.ReadScopeTools;

        // Grants only ever narrow. A grant outside the capability surface is a dead scope that
        // reads as capability the agent does not have — exactly the confusion the two separate
        // prefixes exist to prevent.
        GrantedTools(client).Should().BeSubsetOf(carried);
    }

    [Fact]
    public void Every_granted_scope_is_declared_as_a_client_scope_and_lands_in_the_token()
    {
        // A scope assigned but never declared is dropped silently on import, and one declared
        // without include.in.token.scope never reaches the "scope" claim the server reads. Either
        // way the agent would be narrowed to nothing it asked for — so both are asserted here.
        var declared = Realm.RootElement.GetProperty("clientScopes").EnumerateArray()
            .Where(s => s.GetProperty("name").GetString()!.StartsWith(GrantPrefix, StringComparison.Ordinal))
            .ToDictionary(s => s.GetProperty("name").GetString()!);

        using var _ = new AssertionScope();
        foreach (var scope in Clients.SelectMany(DefaultScopes)
                     .Where(s => s.StartsWith(GrantPrefix, StringComparison.Ordinal)).Distinct().Order())
        {
            declared.Should().ContainKey(scope, "an undeclared client scope is dropped on realm import");
            if (declared.TryGetValue(scope, out var definition))
            {
                definition.GetProperty("attributes").GetProperty("include.in.token.scope").GetString()
                    .Should().Be("true", $"{scope} is only a grant if it reaches the token's scope claim");
            }
        }
    }

    [Fact]
    public void No_client_scope_is_declared_that_nothing_is_granted()
    {
        // The other direction: a grant scope no client holds is a tool nobody may use, which is
        // either a leftover or a rename half-done.
        var assigned = Clients.SelectMany(GrantedTools).ToHashSet(StringComparer.Ordinal);
        var declared = Realm.RootElement.GetProperty("clientScopes").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()!)
            .Where(n => n.StartsWith(GrantPrefix, StringComparison.Ordinal))
            .Select(n => n[GrantPrefix.Length..]);

        declared.Should().BeEquivalentTo(assigned);
    }

    [Fact]
    public void The_human_and_e2e_clients_carry_no_grants_and_so_keep_the_whole_surface()
    {
        // expert-to-job-mcp is the interactive PKCE client a person drives, and expert-to-job-e2e
        // exercises the whole tool surface on purpose. Narrowing is opt-in, and neither opts in.
        using var _ = new AssertionScope();
        GrantedTools(Client("expert-to-job-mcp")).Should().BeEmpty();
        GrantedTools(Client("expert-to-job-e2e")).Should().BeEmpty();
    }

    [Fact]
    public void Every_agent_client_in_the_realm_is_narrowed()
    {
        // No agent identity may quietly keep the whole surface: the ungranted default exists so a
        // narrowing is deliberate, not so an agent can be forgotten when it is added to the realm.
        using var _ = new AssertionScope();
        foreach (var client in Clients.Where(c => ClientId(c).StartsWith("agent-", StringComparison.Ordinal)))
        {
            GrantedTools(client).Should().NotBeEmpty($"{ClientId(client)} is a registered MCP identity");
        }
    }
}
