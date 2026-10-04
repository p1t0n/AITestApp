# The production Keycloak realm and image

EXP-117. The local stack and the deployed stack run the **same Keycloak version** against **two
realms**: `keycloak/realm-export.json`, which the AppHost imports and every test has always run
against, and `keycloak/realm-export.prod.json`, which `keycloak/Dockerfile` bakes into the image a
deployment runs. The second is generated from the first, so they cannot drift apart silently.

```
keycloak/realm-export.json          # the dev realm — edited by hand, imported by the AppHost
  └─ tools/MakeProdRealm            # dotnet run --project tools/MakeProdRealm
       └─ keycloak/realm-export.prod.json   # generated, checked in, COPYd by keycloak/Dockerfile
```

## What the transform changes, and nothing else

`tools/KeycloakRealm.Core/ProdRealmTransform.cs` makes three edits.
`ProdRealmTransformTests.Nothing_else_in_the_realm_changed` is a path-by-path diff of the two
trees asserting that the set of differences is exactly these and no others — so a fourth edit
slipped in by accident is a red test, not a surprise in production.

1. **The dev-only clients are dropped.**
   * `expert-to-job-e2e` is a confidential client holding `mcp:admin` and a committed secret. It
     exists so the test suite can obtain a fully scoped token. On a public deployment it is a
     back door with the key under the mat.
   * `expert-to-job-mcp` is the interactive public PKCE client a person drives, and all four of
     its redirect URIs are loopback. Strip those — which rule 2 would — and what is left is a
     standard-flow client that can complete no flow at all. It is also frequently assumed to own
     the `mcp-audience` mapper; it does not. That mapper lives on the **client scope** of the same
     name, which every agent client carries by default and which the transform leaves alone.
     `KeycloakProdRealmImageE2ETests.The_token_still_carries_the_audience_the_mcp_server_validates`
     is the proof: with the client gone, an `agent-roster-qa` token still carries
     `aud: https://localhost/mcp`, which is what `Mcp:Resource` validates.

     A remote MCP client reaches the deployed realm through **Dynamic Client Registration**, which
     the `mcp-dcr-oauth-2-1` profile and the `mcp-dynamically-registered-clients` policy govern and
     which stays in the export (`manuals/mcp-dcr-policy.md`).

2. **Every remaining loopback redirect URI and web origin is removed.** Written as a sweep over
   all clients rather than as a fact about today's two, so a client added to the dev realm next
   month cannot carry `http://localhost/*` into a public deployment because the rule only knew
   about yesterday's list.

3. **Every remaining client `secret` becomes an environment placeholder.** `agent-roster-qa` →
   `${AGENT_ROSTER_QA_SECRET}`, derived from the client id (upper-case, dashes to underscores,
   `_SECRET`). Unconditional rather than "if it looks like a dev constant": the guarantee is that
   *no* value from the source file reaches the output, and re-running the generator over its own
   output is a no-op rather than `${${...}}`.

## The placeholder syntax, and why it is the bare one

Keycloak 26 resolves `${NAME}` in a realm import file from `System.getenv("NAME")`.

* Doc: <https://www.keycloak.org/server/importExport> — *"Using Environment Variables within the
  Realm Configuration Files: You are able to use placeholders to resolve values from environment
  variables for any realm configuration"*, with the example `"realm": "${MY_REALM_NAME}"`.
* Code: `exportimport/AbstractFileBasedImportProvider` installs a resolver that is literally
  `System::getenv` over the raw property name, and `ExportImportManager.runImportAtStartup` is
  what turns replacement on for `--import-realm` (it is off by default).

**`${env.NAME}` does not work here**, even though that spelling works in other Keycloak contexts
(`SystemEnvProperties` strips an `env.` prefix; the import resolver does not). Measured on
`keycloak:26.0`, a realm with two clients and `PROBE_SECRET=from-the-environment` in the
environment:

| client secret in the import file | authenticates with |
|---|---|
| `${PROBE_SECRET}` | `from-the-environment` (200); the literal string → 401 |
| `${env.PROBE_SECRET}` | the literal string `${env.PROBE_SECRET}` (200); the env value → 401 |

### An unset variable fails open

`StringPropertyReplacer` leaves an unresolved `${NAME}` **unchanged** — it does not fail the
import. So a deployment that forgets `AGENT_MATCH_SECRET` does not crash; it creates
`agent-match` with the client secret `${AGENT_MATCH_SECRET}`, a string printed in this public
repository. Whatever starts this image must supply all eight:

```
AGENT_ROSTER_QA_SECRET        AGENT_INTERVIEW_KIT_SECRET
AGENT_CV_TAILORING_SECRET     AGENT_BENCH_REPORT_SECRET
AGENT_MATCH_SECRET            AGENT_RESUME_INGESTION_SECRET
AGENT_SHORTLIST_SECRET        AGENT_ROSTER_SCAN_SECRET
```

These are Keycloak's names for the secrets. The Agents host reads the *same eight values* under
its own configuration keys — `McpAuth:<agent-key>:ClientSecret`, i.e.
`McpAuth__roster-qa__ClientSecret` as an environment variable — and `api/Agents/Program.cs`
already refuses to start when one is empty. The deployment has to set both halves from one source.

## The image

`keycloak/Dockerfile` is the official `quay.io/keycloak/keycloak:26.0` plus one `COPY`. The tag
matches the one `api/AppHost/Program.cs` runs locally on purpose: the realm it imports is
generated from the realm the AppHost imports, so a tag skew would mean the deployed authorization
server behaves differently from the one every test ran against.

```
CMD ["start", "--import-realm", "--http-enabled=true", "--proxy-headers=xforwarded", "--health-enabled=true"]
```

* `start`, not `start-dev` — production mode.
* `--http-enabled=true` because TLS terminates at the edge; this listens on plain HTTP inside the
  network.
* `--proxy-headers=xforwarded` because without it Keycloak builds issuer and redirect URLs from
  the internal address, and every token it mints then names an issuer nobody can reach.
* `--health-enabled=true` puts `/health/ready` on the **management port 9000**. It never appears
  on 8080 — the probe that points at 8080 is the mistake this flag invites.

Nothing about *where* it runs is baked in. `KC_HOSTNAME`, the database (`KC_DB`, `KC_DB_URL`,
`KC_DB_USERNAME`, `KC_DB_PASSWORD`), the admin bootstrap (`KC_BOOTSTRAP_ADMIN_*`) and the eight
agent secrets all arrive as environment variables, so the image is the same artifact everywhere
and holds no secret. There is deliberately **no `kc.sh build`** step: `KC_DB` is a build-time
option, so baking it would nail the database vendor into the image. `start` re-augments on boot
instead, which costs a few seconds.

`keycloak/.dockerignore` excludes `realm-export.json` from the build context, so the dev realm —
which still carries the eight dev secrets — cannot reach a layer even through a mistaken `COPY`.

## Why the generated file is checked in

It could have been git-ignored and produced at build time. It is checked in because:

* `keycloak/Dockerfile` `COPY`s it. A git-ignored build input makes a clean checkout unbuildable
  and pushes "run the generator first" into every pipeline that touches the image.
* What lands in production is then reviewable as a **diff**. `git diff keycloak/realm-export.json
  keycloak/realm-export.prod.json` is a dozen lines a human can read; the output of a script
  nobody ran is not.

The cost is drift, and `ProdRealmTransformTests.Committed_prod_realm_is_exactly_what_the_transform_produces`
pays it: byte-for-byte, not JSON-equal, because whitespace matters to a reviewer too. Editing the
dev realm without regenerating is a red test. `dotnet run --project tools/MakeProdRealm --check`
reports drift without writing, which is the form a deploy pipeline wants.

## Where the tests are, and which run when

| Suite | Needs Docker | Runs in `Category!=e2e&Category!=live` |
|---|---|---|
| `tests/Mcp.Tests/ProdRealmTransformTests.cs` | no | **yes** — the transform is a pure function and both realms are JSON on disk |
| `tests/Mcp.Tests/KeycloakProdRealmImageE2ETests.cs` | yes — it builds the image | no, `Category=e2e` |

The e2e one builds a Docker image, which is precisely what the local loop's filter exists to keep
out. It still runs on **every CI run**: `.github/workflows/ci.yml`'s "Build & Test" job runs
`dotnet test` with no filter, next to `KeycloakE2ETests` and `KeycloakDcrE2ETests`. No new
category was added and the workflow is unchanged.

It uses `KC_DB=dev-file` rather than a Postgres container. Nothing it asserts touches the
database: substitution happens on the file before the import, and a client-credentials grant reads
the same imported client record whatever the JDBC driver underneath is. A second container would
add startup time and a second failure mode to a test that measures neither.
