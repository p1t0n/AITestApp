using System.Text.Json;
using System.Text.Json.Nodes;
using ExpertToJob.KeycloakRealm;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace ExpertToJob.Mcp.Tests;

/// <summary>
/// The production realm (EXP-117) is <c>keycloak/realm-export.json</c> with three edits and
/// nothing else: the dev-only clients are gone, no client points at a loopback redirect, and every
/// client secret is an environment placeholder rather than the committed dev constant.
///
/// <para>These are deterministic — the transform is a pure string-to-string function and both
/// files are JSON on disk, so nothing here needs Docker. That a real Keycloak 26 then
/// <em>resolves</em> those placeholders is <see cref="KeycloakProdRealmImageE2ETests"/>, which
/// does.</para>
///
/// <para>The generated file is <strong>checked in</strong>, and
/// <see cref="Committed_prod_realm_is_exactly_what_the_transform_produces"/> is the drift test
/// that keeps it honest. Checked in rather than git-ignored because
/// <c>keycloak/Dockerfile</c> <c>COPY</c>s it: a git-ignored input would make a clean checkout
/// unbuildable, and because what lands in production is then reviewable as a diff instead of as
/// the output of a script nobody ran.</para>
/// </summary>
public class ProdRealmTransformTests
{
    private static readonly string Source =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "realm-export.json"));

    private static readonly string Committed =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "realm-export.prod.json"));

    private static JsonNode Prod() => JsonNode.Parse(ProdRealmTransform.Apply(Source))!;

    private static JsonNode SourceNode() => JsonNode.Parse(Source)!;

    private static IEnumerable<JsonNode> Clients(JsonNode realm) =>
        realm["clients"]!.AsArray().Select(c => c!);

    private static string ClientId(JsonNode client) => client["clientId"]!.GetValue<string>();

    [Fact]
    public void Committed_prod_realm_is_exactly_what_the_transform_produces()
    {
        // Byte-for-byte, not JSON-equal: the committed file is a build input for the Dockerfile and
        // a review surface for a human, so whitespace drift matters too. Regenerate with
        // `dotnet run --project tools/MakeProdRealm`.
        Committed.Should().Be(
            ProdRealmTransform.Apply(Source),
            "keycloak/realm-export.prod.json is generated — rerun `dotnet run --project tools/MakeProdRealm`");
    }

    [Fact]
    public void The_e2e_only_client_is_gone()
    {
        Clients(Prod()).Select(ClientId).Should().NotContain("expert-to-job-e2e");
    }

    [Fact]
    public void The_interactive_pkce_client_is_gone()
    {
        // Its audience was never its own: `mcp-audience` is a *client scope* carried by every agent
        // client, so dropping the client costs no token claim (proven by the aud assertion in the
        // e2e). What is left of it without its loopback redirects is a standard-flow public client
        // that cannot complete a redirect — dead weight on a public realm. A remote MCP client
        // reaches this realm through Dynamic Client Registration, which `mcp-dcr-oauth-2-1`
        // governs and which stays in the export.
        Clients(Prod()).Select(ClientId).Should().NotContain("expert-to-job-mcp");
    }

    [Fact]
    public void No_client_points_at_a_loopback_address()
    {
        using var _ = new AssertionScope();
        foreach (var client in Clients(Prod()))
        {
            foreach (var key in new[] { "redirectUris", "webOrigins" })
            {
                var uris = client[key]?.AsArray().Select(u => u!.GetValue<string>()) ?? [];
                foreach (var uri in uris)
                {
                    uri.Should().NotContainAny(["localhost", "127.0.0.1", "[::1]"],
                        because: $"{ClientId(client)}.{key} must not survive into production");
                }
            }
        }
    }

    [Fact]
    public void Not_one_dev_secret_survives_anywhere_in_the_output()
    {
        // Compared against *every* `secret` in the source, not just the eight agent ones: a client
        // added to the dev realm tomorrow must not be able to ship its constant to production
        // because this test only knew about yesterday's list.
        var devSecrets = Clients(SourceNode())
            .Select(c => c["secret"]?.GetValue<string>())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        devSecrets.Should().HaveCountGreaterThan(1, "the source realm ships dev secrets to strip");

        var prodText = ProdRealmTransform.Apply(Source);
        using var _ = new AssertionScope();
        foreach (var secret in devSecrets)
        {
            prodText.Should().NotContain(secret!);
        }
    }

    [Fact]
    public void Every_surviving_client_secret_is_an_environment_placeholder()
    {
        var withSecrets = Clients(Prod())
            .Where(c => c["secret"] is not null)
            .ToList();

        // The eight agent-* clients are the whole confidential surface once the dev pair is gone.
        withSecrets.Select(ClientId).Should().BeEquivalentTo([
            "agent-roster-qa", "agent-cv-tailoring", "agent-match", "agent-shortlist",
            "agent-interview-kit", "agent-bench-report", "agent-resume-ingestion",
            "agent-roster-scan",
        ]);

        using var _ = new AssertionScope();
        foreach (var client in withSecrets)
        {
            var expected = "${" + ProdRealmTransform.SecretEnvVar(ClientId(client)) + "}";
            client["secret"]!.GetValue<string>().Should().Be(expected);
        }
    }

    [Theory]
    [InlineData("agent-roster-qa", "AGENT_ROSTER_QA_SECRET")]
    [InlineData("agent-resume-ingestion", "AGENT_RESUME_INGESTION_SECRET")]
    public void The_placeholder_name_is_derived_from_the_client_id(string clientId, string expected)
    {
        ProdRealmTransform.SecretEnvVar(clientId).Should().Be(expected);
    }

    [Fact]
    public void The_mcp_audience_mapper_is_untouched()
    {
        // `https://localhost/mcp` is the RFC 8707 audience string the MCP resource server checks,
        // not a redirect URI — the loopback sweep above must not have taken it with it.
        var scope = Prod()["clientScopes"]!.AsArray()
            .Single(s => s!["name"]!.GetValue<string>() == "mcp-audience")!;

        scope["protocolMappers"]![0]!["config"]!["included.custom.audience"]!
            .GetValue<string>().Should().Be("https://localhost/mcp");
    }

    [Fact]
    public void Nothing_else_in_the_realm_changed()
    {
        // A real diff rather than a second copy of the transform: walk both trees, collect every
        // JSON path whose value differs or is missing on one side, and assert the set is exactly
        // the three documented edits. Anything the transform does by accident shows up here.
        var differences = Diff(SourceNode(), Prod(), "$").ToList();

        differences.Should().BeEquivalentTo([
            "$.clients[expert-to-job-mcp]: removed",
            "$.clients[expert-to-job-e2e]: removed",
            "$.clients[agent-roster-qa].secret",
            "$.clients[agent-cv-tailoring].secret",
            "$.clients[agent-match].secret",
            "$.clients[agent-shortlist].secret",
            "$.clients[agent-interview-kit].secret",
            "$.clients[agent-bench-report].secret",
            "$.clients[agent-resume-ingestion].secret",
            "$.clients[agent-roster-scan].secret",
        ]);
    }

    /// <summary>
    /// Paths where <paramref name="right"/> differs from <paramref name="left"/>. Arrays of
    /// clients are matched by <c>clientId</c> rather than by index so that removing one does not
    /// read as "every client after it changed".
    /// </summary>
    private static IEnumerable<string> Diff(JsonNode? left, JsonNode? right, string path)
    {
        if (left is null || right is null)
        {
            if (!ReferenceEquals(left, right))
            {
                yield return path;
            }

            yield break;
        }

        if (left is JsonObject lo && right is JsonObject ro)
        {
            foreach (var key in lo.Select(p => p.Key).Union(ro.Select(p => p.Key)))
            {
                foreach (var d in Diff(lo[key], ro[key], $"{path}.{key}"))
                {
                    yield return d;
                }
            }

            yield break;
        }

        if (left is JsonArray la && right is JsonArray ra)
        {
            if (path == "$.clients")
            {
                var byId = ra.ToDictionary(c => ClientId(c!));
                foreach (var client in la)
                {
                    var id = ClientId(client!);
                    if (!byId.TryGetValue(id, out var match))
                    {
                        yield return $"{path}[{id}]: removed";
                        continue;
                    }

                    foreach (var d in Diff(client, match, $"{path}[{id}]"))
                    {
                        yield return d;
                    }
                }

                foreach (var id in byId.Keys.Except(la.Select(c => ClientId(c!))))
                {
                    yield return $"{path}[{id}]: added";
                }

                yield break;
            }

            for (var i = 0; i < Math.Max(la.Count, ra.Count); i++)
            {
                foreach (var d in Diff(
                    i < la.Count ? la[i] : null, i < ra.Count ? ra[i] : null, $"{path}[{i}]"))
                {
                    yield return d;
                }
            }

            yield break;
        }

        if (left.ToJsonString() != right.ToJsonString())
        {
            yield return path;
        }
    }

    [Fact]
    public void The_transform_is_idempotent()
    {
        // Running the generator twice must not double-wrap a placeholder into ${${...}}.
        var once = ProdRealmTransform.Apply(Source);
        ProdRealmTransform.Apply(once).Should().Be(once);
    }

    [Fact]
    public void The_committed_prod_realm_is_valid_json_the_import_can_read()
    {
        var act = () => JsonDocument.Parse(Committed);
        act.Should().NotThrow();
    }
}
