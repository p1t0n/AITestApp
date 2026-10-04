using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExpertToJob.KeycloakRealm;

/// <summary>
/// Turns the development realm (<c>keycloak/realm-export.json</c>) into the one the production
/// image imports (<c>keycloak/realm-export.prod.json</c>) — EXP-117.
///
/// <para>Three edits and no others, each of which <c>ProdRealmTransformTests</c> pins:</para>
/// <list type="number">
///   <item>the dev-only clients are dropped — <c>expert-to-job-e2e</c> (a confidential client with
///   <c>mcp:admin</c> and a committed secret, which exists for the test suite) and
///   <c>expert-to-job-mcp</c> (a public PKCE client whose only redirect URIs are loopback, so with
///   those stripped it can complete no flow at all; the <c>mcp-audience</c> mapper it is sometimes
///   assumed to own actually lives on the shared client scope, which stays);</item>
///   <item>any remaining loopback redirect URI or web origin is removed, so a client added to the
///   dev realm later cannot carry <c>http://localhost/*</c> into a public deployment;</item>
///   <item>every surviving <c>secret</c> becomes a Keycloak import placeholder naming an
///   environment variable, so the image carries no secret and the deployment supplies them.</item>
/// </list>
///
/// <para><strong>Placeholder syntax.</strong> Keycloak 26 resolves a bare <c>${NAME}</c> in a
/// realm import file against <c>System.getenv("NAME")</c> —
/// <c>exportimport/AbstractFileBasedImportProvider</c> installs exactly that resolver, and
/// <c>ExportImportManager.runImportAtStartup</c> is what turns replacement on for
/// <c>--import-realm</c>. The <c>${env.NAME}</c> spelling that works elsewhere in Keycloak does
/// <em>not</em> work here. Measured on <c>keycloak:26.0</c>: a client whose secret was
/// <c>${env.PROBE_SECRET}</c> authenticated with the literal string <c>${env.PROBE_SECRET}</c>,
/// while <c>${PROBE_SECRET}</c> authenticated with the environment's value. Docs:
/// https://www.keycloak.org/server/importExport — "Using Environment Variables within the Realm
/// Configuration Files".</para>
///
/// <para><strong>An unset variable fails open.</strong> <c>StringPropertyReplacer</c> leaves an
/// unresolved <c>${NAME}</c> unchanged, so a missing environment variable makes the client secret
/// the literal placeholder text — which is in this public repository. Whatever starts this image
/// must supply all eight; <c>manuals/keycloak-prod-realm.md</c> carries the detail.</para>
/// </summary>
public static class ProdRealmTransform
{
    public const string SourceFileName = "realm-export.json";

    public const string ProdFileName = "realm-export.prod.json";

    /// <summary>Clients that exist for local development and the test suite, and nothing else.</summary>
    public static readonly string[] DevOnlyClients = ["expert-to-job-e2e", "expert-to-job-mcp"];

    /// <summary>Hosts a production realm must never redirect to or accept an origin from.</summary>
    private static readonly string[] Loopback = ["localhost", "127.0.0.1", "[::1]"];

    /// <summary>Client properties that hold redirect/origin URIs.</summary>
    private static readonly string[] UriArrays = ["redirectUris", "webOrigins"];

    /// <summary>
    /// WriteIndented matches the source's two-space layout and the relaxed encoder leaves
    /// <c>&amp;</c>, <c>'</c>, <c>+</c> and the em dashes in the client descriptions as themselves,
    /// so <c>git diff realm-export.json realm-export.prod.json</c> reads as the three edits rather
    /// than as a reformat. "Unsafe" there means unsafe to interpolate into HTML; this file is read
    /// by Keycloak's realm import and by nothing else.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The environment variable a client's secret is read from: the client id upper-cased with
    /// dashes as underscores, plus <c>_SECRET</c>. <c>agent-roster-qa</c> →
    /// <c>AGENT_ROSTER_QA_SECRET</c>.
    /// </summary>
    public static string SecretEnvVar(string clientId) =>
        clientId.ToUpperInvariant().Replace('-', '_') + "_SECRET";

    /// <summary>
    /// Applies the transform. Pure: string in, string out, so the generator and the drift test
    /// run the same code and neither needs a file system fixture.
    /// </summary>
    public static string Apply(string sourceJson)
    {
        var realm = JsonNode.Parse(sourceJson)?.AsObject()
                    ?? throw new InvalidOperationException("The realm export is not a JSON object.");

        if (realm["clients"] is not JsonArray clients)
        {
            throw new InvalidOperationException("The realm export has no `clients` array.");
        }

        for (var i = clients.Count - 1; i >= 0; i--)
        {
            var client = clients[i]!.AsObject();
            var clientId = client["clientId"]?.GetValue<string>()
                           ?? throw new InvalidOperationException($"Client at index {i} has no clientId.");

            if (DevOnlyClients.Contains(clientId))
            {
                clients.RemoveAt(i);
                continue;
            }

            StripLoopback(client);
            PlaceholderiseSecret(client, clientId);
        }

        // "\n", not Environment.NewLine: the committed file is compared byte-for-byte by the
        // drift test, and a generator that emits CRLF on one machine turns that test into a
        // platform check.
        return JsonSerializer.Serialize(realm, SerializerOptions) + "\n";
    }

    private static void StripLoopback(JsonObject client)
    {
        foreach (var key in UriArrays)
        {
            if (client[key] is not JsonArray uris)
            {
                continue;
            }

            for (var i = uris.Count - 1; i >= 0; i--)
            {
                var uri = uris[i]?.GetValue<string>() ?? string.Empty;
                if (Loopback.Any(h => uri.Contains(h, StringComparison.OrdinalIgnoreCase)))
                {
                    uris.RemoveAt(i);
                }
            }
        }
    }

    private static void PlaceholderiseSecret(JsonObject client, string clientId)
    {
        if (client["secret"] is null)
        {
            return;
        }

        // Unconditional, not "if it looks like a dev constant": the guarantee wanted is that no
        // value from the source file can reach the output, and re-running the generator over its
        // own output has to be a no-op rather than ${${...}}.
        client["secret"] = "${" + SecretEnvVar(clientId) + "}";
    }
}
