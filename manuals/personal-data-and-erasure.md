# Personal-data stores, and the one path that erases them

> **Status (2026-09-02):** shipped as P1T-186. `PersonalDataDeclaration` is the single list, an
> audit walks the real EF model and fails the build on an undeclared store, and erasure is
> self-service, synchronous and gated by the control word. Decision:
> [P1T-172](https://linear.app/p1t0ns-nest/issue/P1T-172); the export that reads the same
> declaration is [P1T-187](https://linear.app/p1t0ns-nest/issue/P1T-187).

## 1. One declaration, two readers

> **Both readers now exist.** P1T-187's access view reads the same list, and
> `TransparencyTests.Every_store_the_scrub_reaches_is_visible_in_the_access_view` asserts the
> symmetry from it: every store erasure destroys is one the person can see while it exists. See
> `manuals/transparency-and-export.md`.

`api/Application/Compliance/PersonalDataDeclaration.cs` names every store that holds or points at a
person, classified `delete | scrub | keep`, each with the reason in plain words. The erasure path
scrubs from it and the Art. 15 access view will read it too.

Two hand-maintained lists would drift, and the drift would be invisible until an audit — so
`Web.Tests/PersonalDataDeclarationTests` asks the database what tables exist rather than keeping a
list of its own. It flags any entity carrying a `*ExpertId` or `*UserId`, **and any entity that
reaches one through a chain of foreign keys**, and requires each to be declared or exempted with a
written reason.

The transitive half is not decoration. `Achievement` is a person's own writing and carries no id at
all — it hangs off `Experience`, which hangs off `Expert`. A sweep that only read column names would
have called the schema clean while leaving every achievement bullet in the database.

The audit runs against **Postgres, not InMemory**: `AppDbContext` branches on the provider, and
under InMemory `ExpertSearchChunk.Embedding` is `Ignore`d entirely — an in-memory audit could not
see the column holding a vector of every person's CV.

## 2. What happens to each store

| Store | Action | Note |
| --- | --- | --- |
| `Expert` + its six child collections | delete | cascade |
| `Achievement`, `ExperienceSkill` | delete | two hops, through `Experience` |
| `ExpertSearchChunk` + `Embedding` | delete | cascade — the vector goes with the text |
| `ProcessingRecord` | delete | cascade; see §5 |
| `PendingClaim`, `ClaimCode` | delete | cascade from both sides |
| `User`, `PasskeyCredential`, `AgentUsage` | delete | cascade |
| `ScoringJobCandidate` | delete | the row, digest and all — a scan is a working artefact |
| `StaffingProposalCandidate` | scrub | name, title, rationale nulled; `ExpertId` and scores stay |
| `StaffingProposal.PackageJson` | scrub | six named fields nulled, structure untouched |

**Scrubbing is pseudonymisation, not anonymisation** (EDPB GL 01/2025 §22). Where a row survives
with its `ExpertId`, the residue is acknowledged personal data under **Art. 18 restriction** — not
laundered data anybody may call anonymous. No code or document here should imply otherwise.

## 3. The schema defect was only half a defect

`ScoringJobCandidate` and `StaffingProposalCandidate` both held `ExpertId` as a bare `Guid` with no
foreign key, so both survived a deletion untouched. They needed opposite fixes.

- **`ScoringJobCandidate` gets an FK with cascade.** It carries the person's name, title, whole
  career digest and a model-written rationale. A scan is a working artefact, so the row goes.
  The migration deletes the orphans first — every Expert deleted before this slice left rows behind,
  pointing at ids that no longer resolve, and the constraint cannot be created over them. They had
  to go regardless: that is the bug.
- **`StaffingProposalCandidate` deliberately gets none.** The row records a decision a human made
  and has to outlive the person: a cascade would delete the decision, and a restrict would block the
  erasure. Its `ExpertId` is a restricted-processing reference, not a link. The absence is now a
  written decision in `AppDbContext` rather than an oversight.

## 4. The package scrub, and why it walks JSON

`StaffingProposal.PackageJson` holds a `StaffingHandoffDocument` — not opaque jsonb. Personal data
sits in **six** addressable places:

```
report.candidates[].name
report.candidates[].title
report.candidates[].rationale
report.candidates[].match.answer
report.candidates[].shortlist.requirements[].snippet   ← the sixth, and easy to miss
report.recommendation.narrative
```

The sixth is the evidence snippets: verbatim quotes from the person's CV, lifted into somebody
else's decision record. P1T-172 listed five.

`HandoffPackageScrub` walks JSON rather than the typed record because those record types live in the
**Agents** host and the Web host that serves erasure cannot reference them — and a second copy of
them over here is exactly the drift this slice exists to prevent. The guarantee is closed from the
other side instead: `Agents.Tests/HandoffPackageScrubTests` puts a *real* serialized document
through the scrub, brings it back through the *real* `TryDeserialize`, and asserts the document
still parses, that those six fields are gone, that **nobody else in the same document loses
anything**, and that the provenance, slices, inputs and degradations survive intact. An unparseable
column is returned exactly as found: rewriting it blind would destroy a decision record to no
purpose.

## 5. `ProcessingRecord` is deleted, and P1T-172's table said keep

Stated plainly because it is a divergence from the ticket.

The table cannot be scrubbed at all: it carries a `BEFORE UPDATE` trigger, so it is delete-or-
nothing. And P1T-183 built its cascade **for this act**, saying so at the time — *"deleting is
erasure (P1T-186), a different act, and the Expert cascade has to be able to take these rows with
it; refusing it here would make somebody's right to erasure depend on a trigger written for another
purpose."*

Keeping rows about an erased person — to prove we once had a basis for data we no longer hold — is
also not something Art. 17(3) plainly covers. So the cascade takes them, and this note is here so
the next reader finds a decision rather than a discrepancy.

## 5a. Two triggers, one mechanism

Since P1T-188 erasure has a second trigger: a retention period running out. It is deliberately not a
second implementation — `IErasureService.EraseMineAsync` (a person asking, gated by the control
word) and `IRetentionErasure.EraseExpiredAsync` (the clock, with no account and no control word)
share one private core, and a test compares the database residue of both. See
`manuals/retention.md` §4.

## 6. The act itself

**Self-service, synchronous, irreversible, control-word gated.** `POST /api/me/account/erase`, under
`api/me` beside the pause and carrying no id — the row erased is always the caller's own, and no
route names anybody else's.

The control word is the only proof-of-person this service has: there is no email, so no confirmation
link and no way to tell anybody afterwards. It is verified **inside** `ErasureService`, not by the
controller, which is why `IControlWordHasher` moved to `Application.Abstractions` in this slice —
the act that turns on it owns the check. A wrong word touches nothing.

The account and the record go **together, hard**, in one `SaveChanges`. No tombstone: registering
again with the same address is simply a new Expert, which is the payoff — "I deleted myself and came
back" needed no design at all.

**The session dies with the account on both hosts.** Neither host is told anything: both re-read the
account on every request (`SessionRevocation`) and it is no longer there. A `TokenVersion` bump would
be redundant — there is no row left to carry it.

## 7. In-flight work loses to erasure

An open `PendingClaim` cascades away; a pending Art. 22 contest clears with the
`ScoringJobCandidate` row it was set on. Both were requests *by* somebody who has now withdrawn
entirely, so the Service Manager's queue simply loses the items. A decided proposal keeps its
decision, hollowed out.

## 8. How this is kept honest

- `Web.Tests/PersonalDataDeclarationTests` — the model walk, plus the checks that keep it honest: a
  floor on what it found, every declared entity still real, every declared field still a column.
- `Web.Tests/ErasureTests` — completeness against Postgres, driven **off the declaration**: after
  erasure it sweeps every string column of every declared store for the person's own unique name.
  A store added to the declaration tomorrow is checked without anybody editing the test.
- `Web.Tests/ErasureTests.Erasure_takes_the_search_chunks_and_their_embeddings` — the cascade that
  is currently correct by configuration and could be dropped by a future migration in silence.
- `Agents.Tests/HandoffPackageScrubTests` — the typed round-trip described in §4.
- `Agents.Tests/SessionRevocationTests.An_erased_account_takes_its_agent_session_with_it` — the
  second host.
