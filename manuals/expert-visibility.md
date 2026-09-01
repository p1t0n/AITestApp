# Visibility: the pause, the Art. 22 route, and the one seam both ride

> **Status (2026-09-01):** shipped as P1T-185. `Expert.HiddenAt` is a nullable timestamp,
> `RosterVisibility` is the only place either predicate is written, and a source-level test fails
> the build if a second one appears. Decisions:
> [P1T-170](https://linear.app/p1t0ns-nest/issue/P1T-170),
> [P1T-179](https://linear.app/p1t0ns-nest/issue/P1T-179).
> Ownership: `api/Web/Auth/README.md`. Lawful basis: `manuals/gdpr-processing-basis.md`.

## 1. Two questions that look like one

Ownership (P1T-182) asks **who is asking**. Visibility asks **what the row permits**. They are
separate seams on purpose, and the separation is what makes the agent estate work: an agent is
`Unrestricted` on ownership and still bound by everything here, so a paused Expert vanishes from the
MCP tools without agents needing an identity at all.

`api/Application/Visibility/RosterVisibility.cs` holds both predicates and nothing else holds
either:

| Predicate | Meaning |
| --- | --- |
| `NotHidden` | the person has not paused themselves |
| `HasArt22Route` | their **current** lawful basis is 6(1)(b) — the only one carrying an Art. 22(2) route |

and two populations composed from them:

| Population | Predicates | Who reads it |
| --- | --- | --- |
| `OnTheBench()` | Active + `NotHidden` | search, shortlist, exemplars, matching, the MCP read tools |
| `Scannable()` | `OnTheBench()` + `HasArt22Route` | the Roster Scan's candidate enumeration, and nothing else |

`Scannable` is **composed from** `OnTheBench`, never a parallel definition. A second copy is the
thing that drifts, and `VisibilitySeamTests` asserts the composition in the source text.

## 2. `HiddenAt` is a timestamp, not a third status

A third `ExpertStatus` value was rejected for two concrete reasons rather than on taste:

- it collides with the `Draft → Active` promote path — "promote an inactive draft" means nothing;
- it silently changes what the partial unique index on `Email` enforces, and **P1T-184's
  claim-matching rule depends on that index meaning what it means today**.

So a hidden Expert keeps `Status = Active`, and a Postgres test asserts the uniqueness rule is
untouched by a pause. The timestamp also answers "since when" for free, which the transparency view
has to disclose.

## 3. Blast radius

**Unseen** — roster list (for agents), Command Palette, semantic search, shortlist search, the
lexical quota fallback, style exemplars, Roster Scan enumeration, Match, `cv_get`, every MCP read
tool.

**Seen and marked** — Service Manager surfaces. Staff must be able to tell a paused Expert from one
who never existed; a bench that silently loses somebody is a bench nobody can explain. The roster
row carries a *Paused* chip and the detail page an explanatory banner.

**Kept and filtered, never deleted** — `ExpertSearchChunk` rows *and* their embeddings. Deleting
them would mean re-embedding on unhide, spending the 100/day quota to undo something reversible. A
pause must not cost a paid resource, and `Unhiding_costs_no_embeddings` asserts the embedding
timestamps are untouched across a pause/resume cycle.

> The reconciler (`SearchIndexReconciler`) deliberately does **not** filter on `HiddenAt`. It
> garbage-collects chunks for experts that left the Active set, and a hidden Expert has not left it.
> Filtering there would delete exactly the chunks this design keeps.

### The audience, and why there is one

Most surfaces never ask: search, digests and the scan filter unconditionally, because they are about
*availability for work* no matter who triggered them — a Service Manager running a semantic search
must not get a paused person either.

The record-shaped surfaces do ask, through `IRosterAudienceProvider`, and the answer falls along the
host: the Web API is `Administration` (its roster screens administer the bench, and its one
Expert-facing surface shows a person the row they themselves paused), the MCP server is `Bench`. The
default is `Bench`, so a host that forgets to register anything hides paused people rather than
exposing them.

The Command Palette is the one exclusion done in the SPA rather than in the seam: it filters the
roster list it already has cached. It is a jump-to-a-person surface, and offering somebody there is
offering them for work.

## 4. In-flight work is not retracted

A **running scan** drops a newly-hidden Expert at its next enumeration and **does not rewrite rows
already scored**. A resumed job never re-sweeps by design, so a person who pauses mid-run stays in
that run's candidate list — which is correct: the scoring already happened.

A **pending `StaffingProposal` stays valid and is badged** (`ProposalCandidateResponse.Unavailable`).
Hiding is not a retraction of a decision already put in front of a human, and the decision ledger
keeps its rows. The approver sees what was recommended *and* that acting on it may no longer be
possible.

## 5. The Art. 22 route, and the product consequence

Legitimate interest is **not** among the three Art. 22(2) exceptions, so a row on LI has no route to
automated decision-making at all. Scoring-without-persisting was considered and rejected: the model
call *is* the processing, and "we did not write the row" is not a defence. So the scan's enumeration
carries `Scannable()`, and `roster_digest_list` — which *is* that enumeration — carries it too.

**Stated rather than discovered: an unclaimed bench member is not scanned, and therefore not
considered.** That makes the claim flow (P1T-184) the on-ramp to being considered at all, which is a
better forcing function than a policy nobody reads.

Two consequences worth knowing before they surprise somebody:

- **A freshly seeded database has nothing to scan.** Seed rows are staff-created and honest about
  it, so they sit on LI. To exercise Roster Scan in development, claim a few rows first — issue a
  claim code from the expert's page and redeem it, or approve a claim.
- **The submit-time estimate counts scannable rows** (`IExpertFilterService.CountScannableAsync`),
  not merely eligible ones. A progress bar that starts by overstating its total is a progress bar
  that lies.

## 6. Who may pause

**The Expert, and nobody else.** A Service Manager who wants somebody off the bench deactivates the
account (`User.Status = Deactivated`) — a different mechanism with a different meaning, so there is
never ambiguity about who hid whom. Staff cannot un-hide somebody who hid themselves. Staff keep
full write on CV *content*: this is an exit control, not content, and a paused record stays
editable.

The rule is expressed as **the shape of the API rather than as a check inside it**:
`IExpertVisibilityService` takes the acting account and resolves that account's own row through
`OwnerUserId`. There is no expert id in any signature and no route that names another row
(`POST /api/me/visibility/hide` · `/unhide` · `GET /api/me/visibility`). A rule enforced by a check
is a rule some later path forgets to make.

Pausing twice keeps the first timestamp: "since when" is a fact about the pause, not about the click.

## 7. Pause is not delete, and the UI has to carry that

P1T-171 chose two separate controls precisely so nobody deletes when they meant to pause — and with
no email there is no way to reach somebody who got it wrong. `BenchPauseControl` states the full
consequence before the press: not offered for work, **nothing deleted**, the record stays, come back
whenever. Erasure lives elsewhere on the page and looks like a different kind of thing (P1T-191).

## 8. How the seam is kept single

- `VisibilitySeamTests` reads the `api/` source and fails if `HiddenAt` appears outside a short
  allow-list — the seam, the column, the pause control, and the two projection files. Adding to that
  list should feel like a decision.
- `Mcp.Tests/RosterVisibilityTests` proves the behaviour against real pgvector, including the
  assertion most likely to regress silently: a paused Expert cannot surface from semantic, shortlist
  or lexical retrieval **while their chunks and embeddings are still in the table**.
- `Web.Tests/VisibilityBoundaryTests` proves the boundary: no route pauses somebody else, staff keep
  seeing paused people marked, and the owner still reaches their own paused row.
