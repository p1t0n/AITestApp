---
name: verify
description: Build, launch, and drive this app (Postgres+Keycloak, Web, MCP, Agents, SPA) to verify changes at the real surface — including passkey sign-in and the agent widget.
---

# Verifying CvManager end-to-end

## Launch the stack

```bash
docker compose up -d                       # pgvector Postgres (:5432) + Keycloak (:8080) + Aspire dashboard (:18888)
# If keycloak/realm-export.json changed since the container was created, the realm is STALE:
docker compose up -d --force-recreate keycloak

dotnet build CvManager.slnx          # build ONCE, then run with --no-build
                                           # (three parallel `dotnet run` builds clash on obj/)
GEMINI_API_KEY=<key> ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5069 dotnet run --project api/Web --no-launch-profile --no-build &
GEMINI_API_KEY=<key> ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5100 dotnet run --project api/Mcp --no-launch-profile --no-build &
GEMINI_API_KEY=<key> ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5200 dotnet run --project api/Agents --no-launch-profile --no-build &
cd web && npm run dev &                    # :5173, proxies /api → 5069 and /agents → 5200
```

`ASPNETCORE_ENVIRONMENT=Development` is required — migrations + base seed only run in Development.
Health probes: Web `GET /swagger/index.html` = 200; Mcp `/` = 401; Agents `/` = 404 (both mean "alive").

## Traces & metrics

Open http://localhost:18888 (Aspire dashboard, anonymous). Every agent request renders as one
trace across `cvmanager-agents` and `cvmanager-mcp` (workflow executors, chat spans, MCP RPCs,
SQL); the Metrics page has `gen_ai.client.token.usage` by model. In-memory — restarting the
container clears history. The services run fine when it is down.

## Demo data + embeddings

```bash
dotnet run --project tools/SeedDemoRoster --no-build -- --count 40   # idempotent; --wipe to remove
# The Mcp service's reconcile worker embeds new employees every ~30s. Watch progress:
docker exec cvmanager-db psql -U postgres -d cvmanager -tAc \
  'SELECT count(*) FILTER (WHERE "Embedding" IS NOT NULL) || \'/\' || count(*) FROM "EmployeeSearchChunks";'
```

Don't stop polling at the first `n/n` — the worker may not have projected the new employees yet
(chunk *total* grows first, then embeds).

## Driving the UI (passkey auth!)

For the standard journeys there is now a suite instead of a scratch script — `cd web && npm run
test:e2e` starts its own database, API and SPA (ports 55433 / 5079 / 5174, dev stack untouched) and
drives sign-up, sign-in and the roster in Chromium. See `manuals/playwright-e2e.md`. Reach for the
manual route below when you need to drive something the suite does not cover.

The whole SPA is passkey-gated. Playwright (devDep in `web/`) + a CDP **virtual authenticator**
handles signup headlessly:

```js
const cdp = await context.newCDPSession(page);
await cdp.send("WebAuthn.enable");
await cdp.send("WebAuthn.addVirtualAuthenticator", { options: {
  protocol: "ctap2", transport: "internal", hasResidentKey: true,
  hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true }});
// /signup: fill "Email" + "Control word", click Sign up — passkey ceremony auto-completes.
```

ESM gotcha: import playwright by absolute path from a scratch script
(`import { chromium } from "<repo>/web/node_modules/playwright/index.mjs"`).

Widget selectors: open button aria-label "Open the agents assistant"; tabs "Roster Q&A" /
"Tailor CV" / "Match" / "Shortlist" / "Usage"; shortlist submit button text **"Build shortlist"**;
results appear when "Run full Match" buttons render. Model roundtrip ≈ 10–30s.

## Driving the API directly (skip the browser)

Agents endpoints validate the dev HS256 JWT. Mint one (dev key from `Auth:Jwt` in appsettings,
`sub` must be a real Users.Id for usage metering):

```python
# HS256 over {"sub":<user-guid>,"email":…,"jti":…,"iss":"cvmanager","aud":"cvmanager-app",exp,iat,nbf}
# key: dev-only-insecure-signing-key-change-me-at-least-32-bytes
```

Then `curl -H "Authorization: Bearer $JWT" http://localhost:5200/agents/shortlist -d '{"jobDescription":"…"}'`.

## Gotchas

- `UID` is readonly in zsh — don't use it as a shell variable name.
- Logs to `/tmp/emgr-logs/*.log`; the MCP log shows `"<tool>" completed. IsError = False` per tool
  call — the ground truth for "did the agent actually call the tool".
- Keycloak has no persistent volume, but the container itself persists — realm changes need
  `--force-recreate keycloak` (symptom: agent token requests return 401).
- The local `api/Agents/appsettings.json` may carry a dev PAT (or see `git stash list`); prefer
  `GEMINI_API_KEY` env. Never commit it.
