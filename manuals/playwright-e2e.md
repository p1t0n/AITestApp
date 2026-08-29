# Playwright e2e (P1T-141)

P1T-140 covered the Web API up to its HTTP boundary. Everything above that — the SPA, and in
particular the passkey ceremonies every user has to pass through — was covered only by `vitest`
component tests that mock the API and never run WebAuthn. This records the browser layer that
closes it.

## Shape

```
web/
  playwright.config.ts   Chromium only, one worker, the SPA as its webServer
  e2e/
    run.mjs              owns the stack for one run: container → API → playwright
    passkey.ts           the virtual authenticator, and signup through the real UI
    auth.e2e.ts          the gate, registration, the return visit, a rejected sign-in
    roster.e2e.ts        create a CV in the UI, list it, open it, download its PDF
```

`npm run test:e2e` is the whole entry point. It needs Docker and nothing else running.

## Decisions

**The run owns its stack, on its own ports.** `e2e/run.mjs` starts a throwaway `pgvector` container
on `:55433` with its own `cvmanager_e2e` database, then the Web API on `:5079`, then Playwright,
which starts the SPA on `:5174`. A dev stack on the usual ports (5432 / 5069 / 5173) is untouched,
and so is the dev database — a suite that could delete a developer's roster would be a suite people
turn off. The container is removed on every exit path, including Ctrl-C, and a leftover from an
interrupted run is cleared before the next one starts.

**A script, not Playwright's `webServer`, for the API.** The API cannot boot before its database
exists, and the database is a container this run creates. `webServer` has no way to express that
ordering, so the sequencing lives in one readable script and `webServer` keeps only the SPA, which
has no such dependency.

**Chromium only, and not by preference.** The virtual authenticator that lets a headless test
complete a passkey ceremony (`WebAuthn.addVirtualAuthenticator`) is a Chrome DevTools Protocol
feature. Firefox and WebKit have no equivalent, so cross-browser coverage of a passkey-gated app is
not available at any price here. If the app ever grows a non-passkey entry point, that path could be
tested more broadly.

**The relying party's origin is overridden per run.** The passkey RP checks the browser's origin
against `Auth:Passkey:Origins`, which lists the dev SPA at `:5173`. The runner passes
`Auth__Passkey__Origins__0=http://localhost:5174` so the suite's own SPA port is the accepted
origin, rather than moving the suite onto the dev port and colliding with a running stack.

**One worker, one shared roster.** The tests share a database within a run, so they keep apart by
owning what they create — a fresh account per test (`uniqueEmail`), fresh employees — and never
assert on roster totals. Parallel workers would need a database each; that is a trade worth making
only when the suite is slow enough to care.

**Registration and assertion are asserted separately.** Signing up exercises one half of
`web/src/auth/webauthn.ts`; coming back later exercises the other, through a different server
ceremony and a different challenge. A suite that only signs up would let the return visit — the path
every returning user takes — break silently.

## Verifying the tests can fail

A passing e2e suite is easy to fake, so the authenticator was disabled once deliberately: with
`addVirtualAuthenticator` returning early, the two ceremony tests fail (registration and assertion
both time out waiting for the roster) while the gate test and the rejected-sign-in test still pass.
That is the expected shape — the ceremonies are really being run, not stubbed.

## Worth revisiting

* **Agent-widget journeys.** Deliberately out of scope: they need either a live model (slow, quota
  bound) or a fake transport reachable from the browser. The second is a design decision — where the
  seam goes — not a test to be written in passing.
* **The Agents service is not started.** The widget renders for a signed-in user and its calls to
  `/agents` fail against nothing. Harmless for these journeys; a widget test would have to start it.
* **Trace artifacts.** Traces are kept only for failures and uploaded from CI on failure; they are
  gitignored locally.
* **Reusing an already-running stack.** Rejected for now — the isolation is worth more than the few
  seconds a rerun costs.
