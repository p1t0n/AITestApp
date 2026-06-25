# Authentication architecture

Passwordless auth for the app. Established by P1T-13 (epic) / P1T-17 (this plumbing).

## Model

- **Credential:** WebAuthn passkey only. No passwords anywhere. Library: [fido2-net-lib](https://github.com/passwordless-lib/fido2-net-lib) (`Fido2.AspNet` 4.0.1).
- **Recovery:** a mandatory per-user *control word* (hashed) is the sole account-recovery secret. Lost device → verify email + control word → register a new passkey.
- **Roles:** flat. Any signed-in user may manage any user.
- **Signup:** open self-serve.
- **Email:** identifier only — no verification, no SMTP.

## Session token

The session is a **symmetric (HS256) JWT**. Chosen over cookies because the SPA talks to two
independent services (the Web API and the separate Agents service) and a bearer token is the
simplest thing both can validate without shared cookie/session infrastructure.

| Concern | Where |
|---|---|
| **Issuing** tokens | Web host only (`EmployeeManager.Web.Auth.JwtTokenIssuer`). Login happens here; it owns the DB. |
| **Validating** tokens | Both Web and Agents, via JWT bearer with identical parameters. |
| Claims | `sub` = user id, `email`, `jti`. `sub` lets the Agents service attribute token usage per user (caps epic). |

### Shared configuration (must match across services)

`Auth:Jwt` — `SigningKey` (≥32 bytes), `Issuer`, `Audience`. Present in both `Web/appsettings.json`
and `Agents/appsettings.json`. The dev key is insecure and committed only for local convenience —
**override from a secret store in production.**

The Agents service does **not** reference the Web project (it stays decoupled, reaching data only
through MCP). So the JWT *validation* parameters are duplicated in
`EmployeeManager.Agents.Auth.SessionAuthExtensions` — they must stay in sync with
`EmployeeManager.Web.Auth.AuthServiceCollectionExtensions`. The issuer is **not** duplicated; only
Web mints tokens.

This session JWT is distinct from the **Keycloak / OAuth** tokens the MCP server validates — those
authenticate external AI agents to the MCP tools and are unrelated to end-user login.

## Ceremony challenge handling

WebAuthn ceremonies are two round-trips (options → authenticator → verify). The pending options
(which carry the server challenge) are stashed in a single-use, TTL'd `IChallengeStore`
(`DistributedCacheChallengeStore`, in-memory by default — swap for Redis when multi-instance). The
client gets a ceremony id and posts it back to complete the ceremony.

## Built here (P1T-17) vs later

- **Here:** Fido2 registration, `IJwtTokenIssuer`, `IChallengeStore`, shared JWT validation in both services, `UseAuthentication/UseAuthorization` wired.
- **Later:** signup (P1T-18), signin (P1T-19), recovery (P1T-20) endpoints + UI drive the ceremonies via `IFido2` + `IChallengeStore` + `IJwtTokenIssuer`. The app-wide `[Authorize]` gate is P1T-22.
