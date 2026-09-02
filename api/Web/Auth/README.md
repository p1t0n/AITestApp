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

## Row ownership (P1T-182)

The role answers "which endpoints?". It cannot answer "which rows?" — an Expert reaches the roster's
child endpoints, and eleven of the seventeen name a row by its own id with no expert anywhere in the
URL (`PUT /api/languages/{id}`, `PATCH /api/achievements/{id}`). So there is a second, narrower
question, and it is answered one layer down.

`Expert.OwnerUserId` (nullable, with a **unique partial index where non-null**) is who a row belongs
to. One person, one row, as database truth rather than service convention; any number of rows may
stay unclaimed, and ownership is independent of role — a Service Manager can be on the bench and own
a row too.

`ExpertToJob.Application.Auth.OwnershipScope` is the caller's reach: `Unrestricted` (Service
Managers, and every MCP agent) or `OwnedBy(expertId)` — including `OwnedBy(null)`, a legitimate
state for someone registered whose claim is not approved yet. Each Application service applies it
when loading. Two alternatives were rejected on purpose:

* **A boundary authorization handler** guards one door. The Web API and the MCP server share these
  services, so the check has to live where both pass through.
* **An EF global query filter** would silently rewrite every query in the system, the agents'
  included. A roster-wide search quietly returning one row is a far worse failure than a service that
  forgets, because nothing would ever tell you.

**Out of scope is a 404, never a 403.** A 403 confirms the id exists, and on a roster of consultants
"that id is real" is information about a person. With the scope applied the row simply is not loaded,
`NotFoundException` throws on its own, and `GlobalExceptionHandler` maps it. `OwnedBy(null)` therefore
answers identically everywhere: a pending claim is structurally indistinguishable from no access.

`tests/Application.Tests/OwnershipScopeCoverageTests.cs` is the audit. It reflects over every roster
service in the Application assembly, calls **every** method that addresses a row by id as an Expert
who owns a different row, and requires each one to behave as though the row were not there — then
runs the same calls unrestricted, so a service cannot pass by refusing everybody. A service that
forgets the scope is a silent hole: the call succeeds and the caller gets someone else's data. Two
things also fail it deliberately: a new id parameter the fixture cannot seed, and a new payload type
it cannot build. Skipping a method is exactly the hole being hunted.

Services deliberately outside row ownership carry their reason in that file: accounts (no owner
column), the skill catalog (shared vocabulary, writes refused at the endpoint), the search services
(agent surfaces, roster-wide by definition), and the claim service (below).

## Claims: writing the owner column (P1T-184)

`OwnerUserId` has to be written by somebody, and whoever writes it cannot be scoped by it. Staff act
on rows nobody owns; a person redeeming a claim code reaches the row *because* they do not own it
yet. So `IClaimService` sits outside the ownership scope on purpose, with its reason recorded in the
audit's exemption list, and what guards it is the endpoint policy above it plus the code itself.

For the same reason the unscoped append lives on its own seam, `IOwnershipChangeRecorder`, rather
than as a method on `IProcessingRecordService` — an exception hidden among scoped methods would make
the ownership audit quietly stop meaning what it says.

`ClaimsController` is `AnyRole` at the class with `ServiceManager` on every action except
`POST /api/claims/redeem`. That layering is forced: both policies must pass, so a staff-only class
would refuse the one action an Expert must be able to take. The rules themselves — matching, codes,
revocation, email immutability — are in `manuals/expert-claims.md`.

## Visibility is a second seam (P1T-185)

Ownership answers "who is asking". It cannot answer "what does the row permit" — an agent is
`Unrestricted` here and must still never see an Expert who paused themselves. That is
`ExpertToJob.Application.Visibility.RosterVisibility`: two predicates (`NotHidden`, `HasArt22Route`),
two populations composed from them (`OnTheBench()`, `Scannable()`), and nothing else in the codebase
writes either — a source-level test fails the build if a second `HiddenAt` appears in a query.

Search, digests and the scan filter unconditionally, because availability is not a question about
the caller. The record-shaped surfaces ask `IRosterAudienceProvider`, and the answer falls along the
host: this one registers `AdministrationAudienceProvider` (staff see paused people, marked; and an
Expert reaches the row they themselves paused), the MCP server takes the `Bench` default. The
default is the narrow one, so a host that forgets fails closed. Details in
`manuals/expert-visibility.md`.

## Erasure and the control word (P1T-186)

`IControlWordHasher` moved from this folder into `Application.Abstractions` in that slice, with the
implementation staying here. The reason is a rule rather than tidiness: the control word is the only
proof-of-person this service has — no email means no confirmation link and no way to tell anybody
afterwards — so the two acts that turn on it, recovery and erasure, verify it **themselves** instead
of trusting a caller to have done it. `ErasureService` holds its own gate.

`POST /api/me/account/erase` deletes the account and the roster row together, in one transaction. No
`TokenVersion` bump is needed: both hosts re-read the account on every request, and it is gone.
Details, and the store-by-store declaration behind it, in `manuals/personal-data-and-erasure.md`.

## The retention sweep (P1T-188)

The Web host gained its first `BackgroundService`. It is here rather than in MCP or Agents because
expiry runs the erasure path, and that is registered here alongside the control-word hasher it
depends on — a second way to delete people in another host is exactly the divergence to avoid.

It is **off unless `Retention:Enabled` is set**: for the one job whose normal operation destroys
somebody's data, the safe default is not running.

The other half lives on the way in. `ExpertActivityInterceptor` stamps `Expert.LastActivityAt` when
— and only when — the ownership scope says an Expert is writing their own record. A Service Manager
and every agent resolve to `Unrestricted`, so their writes never move somebody's retention clock.
That is the rule the whole slice turns on; `manuals/retention.md` §2 has the reasoning.

## The SPA's half of the split (P1T-190)

The routes mirror this table. An Expert has two places — `/me/cv` and `/me/privacy` — and the guard
sends a signed-in person who asks for a route their role cannot have to **their own landing page**,
never `/signin`. Nothing there is a security boundary: the server re-decides every request from the
token, and the router only decides which chrome and which page a session gets.
`manuals/spa-architecture.md` §4 has the shape.

### Who reaches what

| Surface | Audience |
|---|---|
| `GET`/`PUT /api/experts/{id}` | Both — the scope decides *which* row |
| The 17 child endpoints | Both — the scope decides, and eleven have no expert in the URL |
| Catalog reads | Both |
| Catalog writes | Service Manager (a category rename rewrites every CV) |
| `POST /api/claims/redeem` | Both — the code is the authorization |
| `/api/me/visibility` (read, hide, unhide) | Both — and always the caller's own row: no id exists |
| `POST /api/me/account/erase` | Both — own account only, gated by the control word |
| `GET /api/me/access`, `GET /api/me/export` | Both — own record only, resolved from the scope |
| `POST /api/experts/{id}/export` | Service Manager — on behalf, and it writes a record |
| `POST /api/contests` | Both — own score only, resolved through the ownership scope |
| `GET /api/contests`, `POST /api/contests/{id}/review` | Service Manager — the human who looks |
| `GET /api/experts`, promote, delete, `cv`, `cv.pdf`, `/api/users` | Service Manager |
| `/api/claims` (queue, approve, reject, codes, revoke) | Service Manager |

`AuthPolicies.AnyRole` is the third explicit audience, for the endpoints both roles genuinely share.
It is still a declaration — the endpoint-classification audit accepts it and nothing else new — and
where two policies meet (a class-level `AnyRole` with a method-level `ServiceManager` on the writes),
both must pass, so the narrower one wins.

## Ceremony challenge handling

WebAuthn ceremonies are two round-trips (options → authenticator → verify). The pending options
(which carry the server challenge) are stashed in a single-use, TTL'd `IChallengeStore`
(`DistributedCacheChallengeStore`, in-memory by default — swap for Redis when multi-instance). The
client gets a ceremony id and posts it back to complete the ceremony.

## Built here (P1T-17) vs later

- **Here:** Fido2 registration, `IJwtTokenIssuer`, `IChallengeStore`, shared JWT validation in both services, `UseAuthentication/UseAuthorization` wired.
- **Later:** signup (P1T-18), signin (P1T-19), recovery (P1T-20) endpoints + UI drive the ceremonies via `IFido2` + `IChallengeStore` + `IJwtTokenIssuer`. The app-wide `[Authorize]` gate is P1T-22.
