---
name: verify
description: Build, launch, and drive this app (Postgres+Keycloak, Web, MCP, Agents, SPA) to verify changes at the real surface — including passkey sign-in and the agent widget.
---

# Verifying ExpertToJob end-to-end

## Launch the stack

```bash
# The Gemini key is optional and lives in user-secrets, not the environment; without it the
# Agents host still starts and the widget degrades.
dotnet user-secrets set Parameters:gemini-api-key <key> --project api/AppHost

dotnet run --project api/AppHost
```

One command brings up everything: pgvector Postgres (:5432), Keycloak (:8080), the one-shot
migrator, Web (:5069), Mcp (:5100), Agents (:5200) and the SPA (:5173) — in that dependency order,
with the three hosts waiting for the migrator to exit cleanly. The AppHost prints the dashboard URL
on startup; open it for per-resource logs, traces and metrics, and to start `demo-roster`.

There is **no stale-realm step**. Keycloak gets a fresh container and a fresh realm import on every
start (it has no data volume, deliberately), so an edit to `keycloak/realm-export.json` takes effect
on the next start and cannot be stale.

Health probes: Web `GET /swagger/index.html` = 200; Mcp `/` = 401; Agents `/` = 404 (both mean
"alive"). The dashboard shows the same thing per resource, which is usually faster than curling.

## Traces & metrics

Open the dashboard URL the AppHost printed. Every agent request renders as one trace across
`experttojob-agents` and `experttojob-mcp` (workflow executors, chat spans, MCP RPCs, SQL); the
Metrics page has `gen_ai.client.token.usage` by model. In-memory — stopping the AppHost clears
history. Each host boots fine with no OTLP endpoint set, which is what a solo `dotnet run` gives it.

## Demo data + embeddings

```bash
dotnet run --project tools/SeedDemoRoster -- --count 40   # idempotent; --wipe to remove
# The Mcp service's reconcile worker embeds new experts every ~30s. Watch progress over the pinned
# port — the AppHost names its containers itself, so `docker exec <a-name-you-guessed>` will not
# find one:
PGPASSWORD=postgres psql -h localhost -p 5432 -U postgres -d experttojob -tAc \
  'SELECT count(*) FILTER (WHERE "Embedding" IS NOT NULL) || \'/\' || count(*) FROM "ExpertSearchChunks";'
```

Don't stop polling at the first `n/n` — the worker may not have projected the new experts yet
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
# HS256 over {"sub":<user-guid>,"email":…,"jti":…,"iss":"experttojob","aud":"experttojob-app",exp,iat,nbf}
# key: dev-only-insecure-signing-key-change-me-at-least-32-bytes
```

Then `curl -H "Authorization: Bearer $JWT" http://localhost:5200/agents/shortlist -d '{"jobDescription":"…"}'`.

## Gotchas

- `UID` is readonly in zsh — don't use it as a shell variable name.
- Per-resource logs are in the dashboard (Console logs → `experttojob-mcp`), and the MCP log shows
  `"<tool>" completed. IsError = False` per tool call — the ground truth for "did the agent
  actually call the tool". A solo `dotnet run` still writes to its own terminal.
- Keycloak gets a new container every start, so realm edits land on the next start — never live.
  (Symptom of running against the old one: agent token requests return 401.)
- The local `api/Agents/appsettings.json` may carry a dev PAT (or see `git stash list`); prefer the
  AppHost's `Parameters:gemini-api-key` user-secret. Never commit it.
- Pinned ports are **proxy** ports: Aspire listens on 5069/5100/5200/5173/5432/8080 and forwards to
  a random port the resource actually bound. `docker ps` shows the random one; it is not the
  contract, and nothing should be read off it.
