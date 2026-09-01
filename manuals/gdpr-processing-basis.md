# Lawful basis, the transparency notice, and the Art. 9 stance

> **Status (2026-09-01):** shipped as P1T-183. `ProcessingRecord` is append-only and per Expert;
> basis is derived from origin and pinned by a database CHECK constraint; the versioned
> transparency notice is acknowledged at registration and the version acknowledged is recoverable.
> Decision: [P1T-171](https://linear.app/p1t0ns-nest/issue/P1T-171). Ownership model:
> [P1T-182](https://linear.app/p1t0ns-nest/issue/P1T-182).

## 1. There is no consent checkbox here, and that is the design

The obvious shape for this — "tick to agree" — is the wrong one, and shipping it would have been
worse than shipping nothing.

Under **Art. 6(1)(b)** a self-registered Expert's data is processed because they asked to be
considered for work; necessity does the legal work and no permission is being sought. Presenting a
consent control where a different basis applies misrepresents what is happening (EDPB GL 05/2020),
and consent that is not real is not a lawful basis — it is a defect wearing the costume of one.

Worse, the basis is effectively **one-shot**. GL 05/2020 §§120, 123: a controller cannot swap away
from consent when it turns out to be invalid, and legitimate interest may not be reached for
retrospectively. Choosing consent here and being wrong is not a thing that can be quietly fixed
later.

What the person actually does is **acknowledge a versioned transparency notice**. That is a real,
recorded, provable act. It is simply not a consent.

## 2. Basis per origin

| Origin | Lawful basis | Art. 20 portability | Art. 21 objection | Art. 22 route |
| --- | --- | --- | --- | --- |
| `SelfRegistered` | **6(1)(b)** pre-contractual measures | **yes** | no — 21(1) covers (e)/(f) only | **22(2)(a)** contract necessity |
| `StaffCreated` | **6(1)(f)** legitimate interest | no | **yes** | **none available** |

The regime is **EU GDPR with CNIL as the interpretive authority**. ICO and CNIL give opposite
answers about a bench — ICO *prohibits* consent for future-role consideration, CNIL *recommends* it
for a CVthèque — and we design to the EU regime because the ICO guidance is still draft and
post-DUAA UK law no longer tracks EU GDPR. CNIL's staffing-agency carve-out under Art. 6(1)(b) is
the route: an Expert registers *in order to be placed on Jobs*, which is a pre-contractual measure
taken at their own request.

**The deciding argument was Art. 22(2)**, which lists the only three routes to lawful automated
decision-making — contract necessity, Union/MS law, explicit consent. Legitimate interest is not
among them. A blanket-LI design would leave the roster scan with no exception at all: not a
safeguards problem, a prohibition.

The consequence follows and is stated rather than discovered later: **staff-created rows sit on LI
and therefore have no Art. 22 exception either.** Mixed basis means mixed exposure. Either the
roster scan must not produce automated outcomes for LI-basis Experts, or the bench migrates toward
self-registration — carried into P1T-179.

## 3. How the pairing is enforced

Three layers, and the last one is the one that matters:

1. `ProcessingRecord.BasisFor(origin)` is the only place a basis is chosen. It throws on an origin
   nobody decided for, rather than falling back.
2. `ProcessingRecord.For(...)` is the only constructor callers use, and it sets `Basis` from
   `Origin`. `LawfulBasis` deliberately has **no zero member**, so a record built by some path that
   skipped the factory carries an undefined value.
3. A Postgres **CHECK constraint**, `CK_ProcessingRecords_BasisMatchesOrigin`, refuses any other
   pairing. A hand-written `INSERT` that bypasses the domain entirely still cannot land a row on a
   ground its origin does not carry.

This is what the acceptance criterion "no global default path exists" means in practice. It is not
a promise about code that exists today; it is a rule the database applies to code that does not
exist yet.

## 4. Append-only, and why

A basis is **superseded by a new row, never rewritten**. Enforced by a `BEFORE UPDATE` trigger on
`ProcessingRecords`, so the rule survives an EF configuration change and a raw `UPDATE` alike.

GL 05/2020 §123 makes the *history* the artefact. "This row was on legitimate interest until March"
is a fact with consequences — an LI row has no Art. 22(2) route, so **it was not scannable in that
window** — and an `UPDATE` would erase that silently. Correcting an error is a new fact, not a
deletion of the old one.

`DELETE` is deliberately **not** blocked. Deleting is erasure (P1T-186), a different act, and the
`Expert` cascade has to be able to take these rows with it; refusing it here would make somebody's
right to erasure depend on a trigger written for another purpose.

`Sequence` (unique per Expert) decides which record is in force. Timestamps tie, and "which basis
applies right now" is a question the Art. 22 route filter has to answer unambiguously.

### The transitions

| Event | Appends | Where it lives |
| --- | --- | --- |
| Expert row created by staff or by an ingestion agent | `StaffCreated` / LI, no notice version | `ExpertService`, in the same transaction as the row |
| Claim on a row approved | `SelfRegistered` / 6(1)(b), with the notice version the claimant acknowledged | P1T-184, via `IProcessingRecordService.AppendAsync` |
| Ownership revoked | `StaffCreated` / LI again | P1T-184 |
| A new notice version acknowledged | the **same** origin, new notice version | `POST /api/notice/acknowledge` |

The last row is the easy mistake: reading an updated notice is not a change in the relationship, so
it must not move the basis. `AcknowledgeNoticeAsync` reads the record in force and appends on the
same origin.

> **Registering does not create a roster row.** Signup makes a `User`; the bench row is staff-created
> and the person *claims* it (P1T-173). So `SelfRegistered` is reached by an approved claim, not by
> a create — which is why every creation path writes `StaffCreated` and that is an origin rather
> than a default.

## 5. Every Expert has a recorded basis

An `Expert` with no `ProcessingRecord` is a compliance defect, so it fails the build rather than an
audit. Two checks, at two altitudes:

- `Application.Tests/ProcessingRecordTests` reflects over `IExpertService` and calls **every** method
  that creates an Expert, requiring each to have written exactly one record. A new creation path is
  covered the moment it exists — the property a hand-kept list cannot have.
- `Web.Tests/ProcessingRecordDatabaseTests` asserts it over the whole live Postgres database: the
  dev seed, the demo roster, and every row the suite created.

The seeders write their own records for the same reason. Seeded rows *are* the Art. 14 population —
nobody registered them and nobody was shown anything — so `NoticeVersion` is null, which is honest
rather than convenient.

## 6. The notice

`Application/Compliance/TransparencyNotice.cs`. Versioned (`2026-09-01`), and **every version ever
shipped stays in the file**: recording a version string proves nothing if the words behind it cannot
be recovered. `GET /api/notice` and `GET /api/notice/{version}` are anonymous by necessity — you
cannot require somebody to acknowledge a text they need an account to read.

What it says is constrained by **Art. 5(1)(a)**: a notice that creates a false impression is itself
a transparency breach. Service Managers keep full write on an Expert's CV and staff-created rows
exist the Expert never authored, so no wording may imply the Expert controls their data. It says
plainly that the company maintains the bench record, that the Expert supplies and corrects their own
content, and that their rights are transparency, erasure and export — **not exclusive authorship**.
It also says out loud that AI scores and ranks them, which is the fact a bench is most tempted to
leave in a footnote.

**Acknowledging blocks registration when declined.** The gate is on `POST /api/auth/signup/begin`,
before the passkey ceremony and before any account exists, so there is no "shown but not
acknowledged" half-state for a downstream surface to reason about. Recording that half-state would
be legally fine — Art. 13 is discharged by *providing* the information — but it costs every later
screen a case to handle for a population of approximately nobody.

**On a new version: notify, don't gate.** `AuthSessionResponse.pendingNoticeVersion` and
`GET /api/notice/status` surface it; `NoticeUpdateBanner` renders it on the Expert's workspace.
Nothing is withheld, nothing is re-collected, nothing is frozen pending a click.

## 7. Two notice gaps we cannot close

Both are real, neither is solved, and they are recorded together so neither reads as an oversight.
**The service never sends email.**

- **Art. 14**: a Service Manager can enter a real person who then must be informed within a month.
  We cannot reach them. They learn we hold them only if they happen to register and claim the row.
- **Art. 13 on change**: an Expert who never signs in again never sees an updated notice.

Both close immediately if email is ever added — which the map rules out permanently.

## 8. Art. 9: a mitigation, not a solution

An Expert may write health, religion or union detail into a summary or an achievement bullet
without intending to. The design response is **minimisation plus a hard prohibition**:

- Guidance text on every free-text field of the CV editor, asking people to leave special-category
  detail out, and naming the categories rather than saying "sensitive information" and stopping
  there (`web/src/pages/cvGuidance.ts`, one string so the ask cannot drift into three wordings).
- The same paragraph inside the transparency notice, so it reaches people who never open the editor.
- **Never build search or filtering on such signals, and never infer protected characteristics.**
  An inference is Art. 9 processing "whether your inference about a candidate is correct or not"
  (ICO). This is a standing constraint on every future retrieval, ranking and scoring feature, not
  a note about the CV form.

Two alternatives were considered and rejected. **Explicit consent under 9(2)(a)**: you cannot
informedly consent to *incidental* content you do not know you are about to write. **Classifying
text on save**: it creates the very Art. 9 inference it aims to avoid.

**Say it plainly: this is a mitigation and not a solution.** Nothing here stops somebody writing
special-category detail into free text, and nothing here removes it once written. The underlying
question — whether unfiltered CV free text is Art. 9 processing at all — stays on the map as needing
a DPO, and this slice does not pretend to close it.
