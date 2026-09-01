# Web API integration tests (P1T-140)

The MCP host and the Agents host were each driven end to end by tests; the Web API was not. Its
controllers, the app-wide authorization gate, the ProblemDetails mapping and everything
Postgres-specific were covered only by inference — Application-layer unit tests on EF InMemory, plus
whatever a human clicked in the SPA. This records the layer that closes that gap, and the two live
defects it found on its first run.

## Shape

```
tests/Web.Tests/
  WebApiFactory.cs          the real host over a throwaway Postgres, + session-token minting
  AuthBoundaryTests.cs      what an unauthenticated caller gets
  ExpertCrudTests.cs      the resource and its children, round trip
  ErrorShapeTests.cs        404 / 400 / 409 shapes
  CvEndpointTests.cs        the CV projection and the PDF render, over the wire
  ApiClientExtensions.cs    test-side helpers
```

## Decisions

**The host boots as it does in development; only the connection string moves.** `WebApiFactory`
overrides `ConnectionStrings:Default` to point at a Testcontainers `pgvector/pgvector:pg17` and
otherwise leaves `Program.cs` alone. The consequence is deliberate: the tests run the real EF
migrations and the real dev seed on startup, so a broken migration fails the suite rather than
production. `UseEnvironment("Development")` is explicit, because that is the environment whose
startup path migrates and seeds, and whose settings carry the dev signing key — booted as
Production the host refuses to start on the placeholder key, by design (P1T-87).

**pgvector, not stock postgres.** The migrations create the `vector` extension for the RAG chunk
store. A plain `postgres:17` image fails at migration time, which is a confusing way to learn this.

**One container and one host for the whole assembly.** Starting Postgres per test class costs more
than the isolation buys. Tests stay apart by owning what they create — a fresh expert per test,
`ApiClientExtensions.UniqueEmail` for addresses — and by never asserting on collection totals. A
test that needs an empty roster does not belong here.

**Real tokens, not a stub authentication handler.** The factory mints an HS256 session token from
the host's own `Auth:Jwt` config, so requests pass the very JWT validation the running app enforces
— the same trick `Agents.Tests/AuthTestExtensions` uses, for the same reason: a fake handler would
pass even if the real validation were broken or removed.

**`ReadOkAsync`, never `ReadFromJsonAsync` on an unchecked response.** Deserializing an error
payload into a DTO yields a default-filled object and the test carries on asserting against nothing.
This is not hypothetical — the first draft of the cascade-delete test did exactly that and hid a
400 (see below). The helper fails with the server's own status and body.

## What the first run found

Both were real, both are fixed in this slice, and neither was reachable from the existing test
layers.

**`ExperiencesController` had no `[ApiController]`.** Without it MVC does not infer `[FromBody]` for
a complex parameter, so `POST /api/experts/{id}/experiences` and `PUT /api/experiences/{id}` bound
an empty `SaveExperienceDto` from the query string and answered 400 to every JSON caller. The
endpoints had been dead since they were written; nothing noticed because the SPA has no experience
edit form yet (still listed as unbuilt) and agents reach experiences over MCP, which calls the
Application layer directly. `Experiences_round_trip_with_their_achievements_from_a_json_body` is the
regression guard.

**A duplicate active email returned 500.** Expert email uniqueness lives in a *partial* unique
index over Active rows — a rule EF cannot pre-check without a race, and one EF InMemory does not
enforce at all. `PromoteAsync` translated the violation into a `ConflictException`; `CreateAsync`,
`UpdateAsync` and `PatchAsync` did not, so an ordinary correctable mistake surfaced as a server
error. All four now save through `ExpertService.SaveGuardingEmailAsync`, which maps the
`IX_Experts_Email` violation to a Conflict with the offending address and a remedy sentence.
REST answers 409; the MCP adapter maps the same exception to its `conflict` error code, so both
surfaces now agree.

## Worth revisiting

* **Test-run time.** The suite adds ~5s (container start plus migrations). If it grows, the answer
  is a snapshot/template database, not more containers.
* **Concurrency.** Nothing here yet exercises two writers racing for the same row or the same email.
  The partial index makes that observable; a test for it would need a second client and deliberate
  interleaving.
* **The seed as a fixture.** Tests currently create their own rows and ignore the dev seed. If a
  test ever needs the seeded catalog, it should read it, not assume its contents — the seed is a
  development convenience and free to change.
* **Playwright e2e** — the other half of PRD §5 item 4 — is not in this slice.
