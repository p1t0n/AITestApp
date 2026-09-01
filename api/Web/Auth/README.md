# Authentication architecture

Passwordless auth for the app. Established by P1T-13 (epic) / P1T-17 (this plumbing).

## Model

- **Credential:** WebAuthn passkey only. No passwords anywhere. Library: [fido2-net-lib](https://github.com/passwordless-lib/fido2-net-lib) (`Fido2.AspNet` 4.0.1).
- **Recovery:** a mandatory per-user *control word* (hashed) is the sole account-recovery secret. Lost device → verify email + control word → register a new passkey.
- **Roles:** two (P1T-181). `ServiceManager` is staff; `Expert` is the person a CV is about. See
  *Roles and revocation* below.
- **Signup:** open self-serve.
- **Email:** identifier only — no verification, no SMTP.

## Session token

The session is a **symmetric (HS256) JWT**. Chosen over cookies because the SPA talks to two
independent services (the Web API and the separate Agents service) and a bearer token is the
simplest thing both can validate without shared cookie/session infrastructure.

| Concern | Where |
|---|---|
| **Issuing** tokens | Web host only (`ExpertToJob.Web.Auth.JwtTokenIssuer`). Login happens here; it owns the DB. |
| **Validating** tokens | Both Web and Agents, via JWT bearer with identical parameters. |
| Claims | `sub` = user id, `email`, `jti`, `role`, `tv`. `sub` lets the Agents service attribute token usage per user (caps epic); `role` is what every authorization policy reads; `tv` is the session generation (see below). |

No inbound claim-type remapping in either host (`MapInboundClaims = false`): the token says `sub`
and `role`, and that is what the app reads. `RoleClaimType`/`NameClaimType` are set to those names
explicitly rather than leaving the legacy WS-\* mapping to rename them behind the code.

### Shared configuration (must match across services)

`Auth:Jwt` — `SigningKey` (≥32 bytes), `Issuer`, `Audience`. Present in both `Web/appsettings.json`
and `Agents/appsettings.json`. The dev key is insecure and committed only for local convenience —
**override from a secret store in production.**

The Agents service does **not** reference the Web project (it stays decoupled, reaching data only
through MCP). So the JWT *validation* parameters are duplicated in
`ExpertToJob.Agents.Auth.SessionAuthExtensions` — they must stay in sync with
`ExpertToJob.Web.Auth.AuthServiceCollectionExtensions`. The issuer is **not** duplicated; only
Web mints tokens.

This session JWT is distinct from the **Keycloak / OAuth** tokens the MCP server validates — those
authenticate external AI agents to the MCP tools and are unrelated to end-user login.

## Roles and revocation (P1T-181)

Two audiences, and the difference between them is the difference between staffing data and one
person's own data:

| Role | Reaches |
|---|---|
| `ServiceManager` | The roster, the skill catalog, user administration, the agent surfaces. |
| `Expert` | Their own data. Nothing on the staff surface. |

**Default-deny, staff by default.** Both hosts set the authorization *default* and *fallback*
policies to `ServiceManager`. So an endpoint that declares nothing is staff-only, and an endpoint
added later is closed to Experts until someone opts it in with
`[Authorize(Policy = AuthPolicies.Expert)]`. `UsersController` is staff-only wholesale — token-cap
fields are staffing data an Expert must not set for themselves; the Expert's own narrow account
surface is a separate slice (P1T-190), not a filtered view of this one.

`tests/Web.Tests/EndpointClassificationTests.cs` walks the host's real `EndpointDataSource` and
fails on any endpoint that does not declare an audience explicitly. A hand-kept list is a list
someone forgets to add to, which is the whole reason the audit reads the route table instead.

**`TokenVersion` is the revocation mechanism.** A signature and a lifetime only say the token was
minted here and has not expired. Whether the session is still *current* is a fact about the
account, so both hosts re-read it on every request from their own JWT bearer event, using the one
shared rule in `ExpertToJob.Application.Auth.SessionRevocation`: the account must still exist, still
be `Active`, and still carry the token version the token was minted with. Bumping the column refuses
every token already issued for that account, which is what erasure will depend on — without it, a
deleted person's session survives up to `AccessTokenMinutes` (now 15, lowered from 60: that window
is the blast radius if the version check ever stops running).

A token with no `tv` claim is refused. Otherwise omitting the claim would be a way to opt out of
revocation entirely.

**Where staff come from.** Signup is open and self-serve, and a self-serve signup is an `Expert`.
The first `ServiceManager` therefore comes from configuration: `Auth:SeedServiceManagerEmail` is
promoted at startup if it already has an account, or given an *invite* row if it does not — an
account with no passkey and no control word, which cannot be signed into. Signup adopts that row
instead of refusing the address as taken, so the operator enrols their own passkey and lands as
staff (`ServiceManagerBootstrapper`, idempotent on every boot). An account with either a credential
or a recovery secret is never adoptable; that would be account takeover by signup.

The migration made every pre-existing account a `ServiceManager`: they were all staff, and demoting
them would have locked everyone out of the app they administer.

**The SPA mirrors this, and only as chrome.** `RequireAuth` takes a required role and each route
declares its own; the rail and ⌘K offer only the places a role can reach. A signed-in user who asks
for a route they cannot have is sent to **their own landing page**, never to `/signin` — telling a
signed-in person they are signed out is both false and a dead end. The server re-decides every
request from the token; nothing stored in the browser is a boundary.

## Ceremony challenge handling

WebAuthn ceremonies are two round-trips (options → authenticator → verify). The pending options
(which carry the server challenge) are stashed in a single-use, TTL'd `IChallengeStore`
(`DistributedCacheChallengeStore`, in-memory by default — swap for Redis when multi-instance). The
client gets a ceremony id and posts it back to complete the ceremony.

## Built here (P1T-17) vs later

- **Here:** Fido2 registration, `IJwtTokenIssuer`, `IChallengeStore`, shared JWT validation in both services, `UseAuthentication/UseAuthorization` wired.
- **Later:** signup (P1T-18), signin (P1T-19), recovery (P1T-20) endpoints + UI drive the ceremonies via `IFido2` + `IChallengeStore` + `IJwtTokenIssuer`. The app-wide `[Authorize]` gate is P1T-22.
