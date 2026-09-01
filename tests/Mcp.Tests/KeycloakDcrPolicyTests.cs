using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// The Dynamic Client Registration ceiling (P1T-157), asserted against the shipped realm.
///
/// <para>Before this, <c>keycloak/realm-export.json</c> declared no registration policy at all,
/// so Keycloak's realm bootstrap created eight of its own on import. The posture of the
/// registration endpoint was therefore a property of the <em>image tag</em> rather than of this
/// repository: a bump from <c>26.0</c> moved it and <c>git diff</c> showed nothing. Declaring the
/// set replaces those defaults outright — verified on a real 26.0, which reports exactly the nine
/// policies below and none of the bootstrapped ones.</para>
///
/// <para>What this holds is the ceiling itself: a client that registers itself may end up holding
/// <c>mcp:read</c> and the audience mapper, and can never register its way to <c>mcp:write</c>,
/// <c>mcp:admin</c> or any per-tool grant. That is product invariant #2 — read-only agents are
/// structurally read-only — written as realm config rather than left as a habit.</para>
///
/// <para>Deterministic: the realm export is JSON on disk (copied beside the test binary for the
/// Keycloak e2e), so nothing here needs Docker. It cannot prove Keycloak <em>honours</em> the
/// declaration — <see cref="KeycloakDcrE2ETests"/> does that against a real server.</para>
/// </summary>
public class KeycloakDcrPolicyTests
{
    private const string PolicyType =
        "org.keycloak.services.clientregistration.policy.ClientRegistrationPolicy";

    private const string Anonymous = "anonymous";
    private const string Authenticated = "authenticated";

    /// <summary>The only client scopes a self-registered client may hold.</summary>
    private static readonly string[] RegistrationCeiling = ["mcp:read", "mcp-audience"];

    /// <summary>
    /// The registration contexts the OAuth 2.1 profile is bound to. <c>ByAuthenticatedUser</c> is
    /// deliberately absent: an operator editing a client through the admin API is not the
    /// untrusted path, and the realm's own imported clients must keep <c>client_secret_basic</c>
    /// and the client-credentials grant that every agent identity runs on.
    /// </summary>
    private static readonly string[] RegistrationContexts =
        ["ByAnonymous", "ByInitialAccessToken", "ByRegistrationAccessToken"];

    private static readonly JsonDocument Realm = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "realm-export.json")));

    private static IEnumerable<JsonElement> Policies =>
        Realm.RootElement.GetProperty("components").GetProperty(PolicyType).EnumerateArray();

    private static IEnumerable<JsonElement> PoliciesFor(string subType) =>
        Policies.Where(p => p.GetProperty("subType").GetString() == subType);

    private static JsonElement Policy(string subType, string providerId) =>
        PoliciesFor(subType).Single(p => p.GetProperty("providerId").GetString() == providerId);

    private static bool HasPolicy(string subType, string providerId) =>
        PoliciesFor(subType).Any(p => p.GetProperty("providerId").GetString() == providerId);

    /// <summary>Keycloak stores every policy config value as a list of strings.</summary>
    private static IReadOnlyList<string> Config(JsonElement policy, string key) =>
        policy.GetProperty("config").TryGetProperty(key, out var value)
            ? value.EnumerateArray().Select(v => v.GetString()!).ToList()
            : [];

    private static IEnumerable<string> DeclaredClientScopes =>
        Realm.RootElement.GetProperty("clientScopes").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()!);

    [Fact]
    public void The_realm_declares_its_registration_policy_instead_of_inheriting_it()
    {
        // Nine, not Keycloak's eight bootstrapped defaults: five for the anonymous path and four
        // for the authenticated one. If this ever reads as the default set, the export lost its
        // `components` block and the posture silently reverted to whatever the image ships.
        Policies.Should().HaveCount(9,
            "the registration endpoint's posture has to be reviewable in this repository, "
            + "not inherited from the Keycloak image tag");
    }

    [Theory]
    [InlineData(Anonymous)]
    [InlineData(Authenticated)]
    public void Neither_registration_path_can_reach_a_scope_above_read(string subType)
    {
        var allowed = Config(Policy(subType, "allowed-client-templates"), "allowed-client-scopes");

        using var _ = new AssertionScope();

        allowed.Should().BeEquivalentTo(RegistrationCeiling,
            "a self-registered client gets read capability plus the audience mapper the MCP "
            + "server validates against, and nothing else");

        // The realm's *default* client-scope list is empty today, which is the only reason
        // `allow-default-scopes: true` was not already a hole. Pinning it false means the ceiling
        // stops depending on nobody ever marking an mcp scope realm-default by convenience.
        Config(Policy(subType, "allowed-client-templates"), "allow-default-scopes")
            .Should().BeEquivalentTo(["false"],
                "the ceiling must be the explicit list, not whatever happens to be realm-default");
    }

    [Theory]
    [InlineData(Anonymous)]
    [InlineData(Authenticated)]
    public void No_write_admin_or_per_tool_scope_is_registrable(string subType)
    {
        var allowed = Config(Policy(subType, "allowed-client-templates"), "allowed-client-scopes");

        using var _ = new AssertionScope();

        allowed.Should().NotContain(McpScopes.Write);
        allowed.Should().NotContain(McpScopes.Admin);

        // Stated as a prefix rule rather than a list of names so it still holds for per-tool
        // grants added later: a grant that arrives after this test was written is covered by it.
        allowed.Should().NotContain(s => s.StartsWith("mcp:tool:", StringComparison.Ordinal),
            "per-tool grants narrow an already-issued capability — handing one to a client that "
            + "registered itself would invert that");

        // Anything the realm declares and the ceiling omits must stay unreachable. This is the
        // test that fails when a new capability scope is added and quietly waved through.
        DeclaredClientScopes.Except(RegistrationCeiling).Should().NotIntersectWith(allowed);
    }

    [Theory]
    [InlineData(Anonymous)]
    [InlineData(Authenticated)]
    public void Both_registration_paths_disable_full_scope_and_bound_the_client_count(string subType)
    {
        using var _ = new AssertionScope();

        // Keycloak ships `scope` and `max-clients` on the anonymous path only, so an initial
        // access token used to produce a client with `fullScopeAllowed: true` — measured on a
        // real 26.0 against the previous export.
        HasPolicy(subType, "scope").Should().BeTrue(
            "a registered client must not inherit the user's full role set");

        Config(Policy(subType, "max-clients"), "max-clients").Should().BeEquivalentTo(["200"],
            "an unbounded registration endpoint is a denial-of-service surface");
    }

    [Fact]
    public void Anonymous_registration_is_closed_and_says_so()
    {
        var trustedHosts = Policy(Anonymous, "trusted-hosts");

        using var _ = new AssertionScope();

        Config(trustedHosts, "host-sending-registration-request-must-match")
            .Should().BeEquivalentTo(["true"]);
        Config(trustedHosts, "trusted-hosts").Should().BeEmpty(
            "no trusted host means every unauthenticated registration is refused — the endpoint "
            + "has always behaved this way, and declaring it makes that a decision rather than "
            + "an accident of Keycloak's defaults");
    }

    [Fact]
    public void Authenticated_registration_is_gated_by_the_token_rather_than_the_host()
    {
        // Layering the host check onto this path turns onboarding off rather than tightening it:
        // measured on a real 26.0, every initial-access-token registration came back
        // "Policy 'Trusted Hosts' rejected ... Host not trusted". The token is the credential here.
        HasPolicy(Authenticated, "trusted-hosts").Should().BeFalse(
            "an initial access token already authenticates the registrar; a host check on top "
            + "rejects every registration and leaves no onboarding path at all");
    }

    [Fact]
    public void Oauth_2_1_is_stamped_onto_a_registered_client_rather_than_remembered()
    {
        var policy = Realm.RootElement.GetProperty("clientPolicies").GetProperty("policies")
            .EnumerateArray().Single();
        var profileName = policy.GetProperty("profiles").EnumerateArray().Single().GetString();
        var profile = Realm.RootElement.GetProperty("clientProfiles").GetProperty("profiles")
            .EnumerateArray().Single(p => p.GetProperty("name").GetString() == profileName);

        using var _ = new AssertionScope();

        policy.GetProperty("enabled").GetBoolean().Should().BeTrue();

        var condition = policy.GetProperty("conditions").EnumerateArray().Single();
        condition.GetProperty("condition").GetString().Should().Be("client-updater-context");
        condition.GetProperty("configuration").GetProperty("update-client-source")
            .EnumerateArray().Select(v => v.GetString()!)
            .Should().BeEquivalentTo(RegistrationContexts,
                "binding this to ByAuthenticatedUser as well would reach the admin API and the "
                + "imported clients, which need client_secret_basic and client-credentials");

        var executors = profile.GetProperty("executors").EnumerateArray().ToList();
        executors.Select(e => e.GetProperty("executor").GetString())
            .Should().BeEquivalentTo(
                ["pkce-enforcer", "reject-implicit-grant", "reject-ropc-grant", "full-scope-disabled"],
                "OAuth 2.1: PKCE required, implicit and password grants gone");

        // auto-configure is what makes this a stamp rather than a runtime check. The executors
        // write onto the client record at registration, so the rules keep holding at token time
        // without a runtime policy that would also have to match the imported clients.
        foreach (var executor in executors)
        {
            executor.GetProperty("configuration").GetProperty("auto-configure").GetBoolean()
                .Should().BeTrue($"{executor.GetProperty("executor").GetString()} has to land on "
                    + "the client itself — a rule that is only checked at runtime is a rule some "
                    + "later condition can stop matching");
        }
    }

    [Fact]
    public void Every_interactive_client_in_the_realm_pins_pkce()
    {
        // The registration policy covers clients that arrive later; this covers the ones shipped
        // in the export, so "OAuth 2.1" holds for both halves of the realm rather than one.
        var interactive = Realm.RootElement.GetProperty("clients").EnumerateArray()
            .Where(c => c.TryGetProperty("standardFlowEnabled", out var f) && f.GetBoolean())
            .ToList();

        interactive.Should().NotBeEmpty();

        using var _ = new AssertionScope();
        foreach (var client in interactive)
        {
            var clientId = client.GetProperty("clientId").GetString();
            client.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString()
                .Should().Be("S256", $"{clientId} runs the authorization-code flow");
        }
    }
}
