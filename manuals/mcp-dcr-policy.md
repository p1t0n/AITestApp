# Dynamic Client Registration policy (P1T-157)

`SPEC.md` called this an OAuth 2.1 deployment and the README advertised Dynamic Client
Registration "for self-service onboarding". Neither claim survived contact with a running
Keycloak. This records what the registration endpoint actually did, what it does now, and why the
tighter version is also the *more useful* one.

Every measurement below came from a real `quay.io/keycloak/keycloak:26.0` started with the shipped
realm and driven through its admin API. None of it came from documentation — `keycloak.org` is
unreachable from this environment, which turned out to be a good constraint: the defaults are not
what a reading of the docs would have suggested.

## What was there

`keycloak/realm-export.json` declared five top-level keys — `realm`, `enabled`,
`accessTokenLifespan`, `clientScopes`, `clients`. No registration policy at all. The imported
realm nonetheless reported **eight** `ClientRegistrationPolicy` components, every one of them
created by Keycloak's own realm bootstrap.

That is the first finding, and it outlives the specific settings: **the posture of the
registration endpoint was a property of the image tag, not of this repository.** A bump from
`26.0` would have moved it and `git diff` would have shown nothing. There was no line to review
and no test to fail.

Underneath that, four concrete problems.

**Anonymous registration never worked.** The default Trusted Hosts policy ships with an empty
host list, so every unauthenticated registration came back:

```
403 {"error":"insufficient_scope","error_description":
     "Policy 'Trusted Hosts' rejected request to client-registration service.
      Details: Host not trusted."}
```

The README's "self-service onboarding" described a path that 403s for every caller, and had done
since the realm was written.

**The authenticated path was the weak one.** Keycloak puts six policies on `anonymous` and two on
`authenticated`. The sub-type reachable with a credential was the one missing `scope`,
`max-clients` and `consent-required`. A registration made with an initial access token therefore
produced:

```json
{"fullScopeAllowed": true, "serviceAccountsEnabled": true,
 "directAccessGrantsEnabled": true, "consentRequired": false, "attributes": {}}
```

The identical request on the *anonymous* path would have been forced to `fullScopeAllowed: false`.
The protection was on the door nobody could open.

**No PKCE.** The registered client carried no `pkce.code.challenge.method` at all, so it could run
authorization-code without a challenge. The shipped `cv-manager-mcp` pins `S256` in its own
attributes, which means PKCE here was a *remembered attribute* — the exact failure mode P1T-149
rejected when it chose request filters over a per-tool `[Authorize]`: forgetting it on client 11
is a silently unprotected client, and an attribute does not know how many clients exist.

**What did hold, held by accident.** Asking for `mcp:admin` was refused —

```
Policy 'Allowed Client Scopes' rejected ... Not permitted to use specified clientScope
```

— but only because `allow-default-scopes: true` permits the realm's *default* client scopes and
that list happens to be empty. One convenience edit marking an `mcp:*` scope realm-default would
have handed it to every registered client, with nothing in the repo standing on it. The same
accident made DCR **decorative**: a self-registered client could obtain no `mcp:*` scope
whatsoever, not even `mcp:read`, so it could call nothing.

## What it is now

Nine registration policies, declared in the export. Declaring them **replaces** Keycloak's
bootstrap rather than merging with it — verified, the imported realm reports exactly these nine
and none of the defaults:

| sub-type | policy | config |
| --- | --- | --- |
| `anonymous` | `trusted-hosts` | no trusted host — registration closed, now by decision |
| `anonymous` · `authenticated` | `allowed-client-templates` | `allow-default-scopes: false`, `allowed-client-scopes: [mcp:read, mcp-audience]` |
| `anonymous` · `authenticated` | `allowed-protocol-mappers` | Keycloak's own default list, verbatim |
| `anonymous` · `authenticated` | `scope` | forces `fullScopeAllowed: false` |
| `anonymous` · `authenticated` | `max-clients` | 200 |

Plus one client profile bound to the three registration contexts:

```
mcp-dcr-oauth-2-1: pkce-enforcer, reject-implicit-grant,
                   reject-ropc-grant, full-scope-disabled   (all auto-configure)
```

A registration that asks for everything now gets a client that has none of it:

```jsonc
// requested: implicitFlowEnabled, directAccessGrantsEnabled, fullScopeAllowed, all true
{"fullScopeAllowed": false, "implicitFlowEnabled": false, "directAccessGrantsEnabled": false,
 "defaultClientScopes": ["mcp-audience", "mcp:read"],
 "attributes": {"pkce.code.challenge.method": "S256"}}
```

## Decisions

**The ceiling is a scope allowlist, not a trust decision.** `allowed-client-scopes` is
`[mcp:read, mcp-audience]` and `allow-default-scopes` is `false`. That is product invariant #2 —
read-only agents are structurally read-only — written as realm config: a client that registered
itself cannot reach `mcp:write`, `mcp:admin` or any `mcp:tool:*` grant, and the refusal does not
depend on the realm-default scope list staying empty. The test states it as a prefix rule rather
than a list of names, so a per-tool grant added later is covered by a test written before it.

**`mcp-audience` is inside the ceiling on purpose.** Without the audience mapper the MCP server
rejects the token whatever its scopes say, so a ceiling of `mcp:read` alone would have left DCR
exactly as decorative as it was. Including it is what turns "tighter" into "tighter *and* it
works": a self-registered agent now gets a token that reads the roster and can do nothing else.
The hardening bought a working onboarding path, which was not the expected direction.

**`trusted-hosts` belongs on `anonymous` and nowhere else.** Adding it to `authenticated` for
symmetry was tried and measured: every initial-access-token registration came back *"Host not
trusted"*, so DCR was off entirely. That is not a tighter policy, it is no policy — the trusted-host
check is the *substitute* for authentication on the anonymous path, and on the authenticated path
the initial access token is already the credential. Symmetry between the two sub-types is not the
goal; each one's gate has to be the thing that path actually lacks.

**The OAuth 2.1 rules are stamped, not checked.** Every executor runs with `auto-configure: true`,
so it writes onto the client record at registration and the rule holds at token time without a
runtime policy. This matters for blast radius: a runtime policy would have to match the imported
clients too, and the global `oauth-2-1-*` profiles that Keycloak ships include
`secure-client-authenticator` and `confidential-client`, which would break every agent identity
(`client_secret_basic` + client-credentials) and the public PKCE client at once. Binding a
realm-local profile to `client-updater-context` instead means the imported clients are never
evaluated — and `The_realms_own_clients_still_authenticate_unchanged` is the e2e test that fails
if that ever stops being true.

**`ByAuthenticatedUser` is deliberately absent from the binding.** The three contexts bound are
`ByAnonymous`, `ByInitialAccessToken` and `ByRegistrationAccessToken` — the registration paths.
An operator creating a client through the admin API is not the untrusted path, and the realm's own
clients must keep the grants they run on.

**`serviceAccountsEnabled` is left alone.** No executor disables it, and with the scope ceiling in
place a service account on a registered client is a read-only service account. Worth revisiting
only if the ceiling ever widens.

## Where the rule lives

| Copy | File | Role |
| --- | --- | --- |
| The declaration | `keycloak/realm-export.json` | `components` + `clientProfiles`/`clientPolicies`. **The boundary.** |
| The assertion | `tests/Mcp.Tests/KeycloakDcrPolicyTests.cs` | Deterministic, over the JSON on disk — no Docker |
| The proof | `tests/Mcp.Tests/KeycloakDcrE2ETests.cs` | `Category=e2e`; a real Keycloak actually obeying it |

The split is the point. A deterministic assertion over JSON can prove the realm *says* the right
thing and can never prove Keycloak *does* it — three of the four findings above were things the
declaration would have looked fine about. Ten of the eleven deterministic tests fail against the
previous export; the eleventh (`Every_interactive_client_in_the_realm_pins_pkce`) passed already
and is there as a guard on the statically-shipped half of the realm, not as a change.

## Changing it

1. Edit the policy in `keycloak/realm-export.json`.
2. `dotnet test --filter "FullyQualifiedName~KeycloakDcrPolicyTests"` — fast, no Docker.
3. `dotnet test --filter "FullyQualifiedName~KeycloakDcrE2ETests"` — ~50s, needs Docker, and is
   the only thing that will tell you Keycloak agrees.
4. Recreate the local container: the realm is imported once, so a running `cvmanager-keycloak`
   keeps the old policy until `docker compose down -v && docker compose up -d`.

## Still open

External IdP (PRD §5.7's other half) is untouched — it needs credentials this environment does not
have. `consent-required` was dropped from both sub-types rather than kept on `anonymous`: with
anonymous registration closed it had nothing to govern, and on the authenticated path a consent
screen does not apply to the client-credentials grant a registered agent runs on. If interactive
DCR clients ever become a real path, that is the decision to revisit first.
