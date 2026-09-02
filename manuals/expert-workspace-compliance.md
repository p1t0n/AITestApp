# The Expert workspace, as compliance: what shipped, and where it left the plan

> **Status (2026-09-02):** shipped as P1T-192, the last slice of the P1T-167 effort. This is the
> narrative of the built design. The per-slice manuals hold the detail; this file holds the shape,
> and the places where what shipped is not what the plan said.
>
> **This is a design input requiring a human sign-off. It is not legal advice.** Every legal claim
> in here is an argument this team wrote down so it could be checked, not an assurance that it
> holds. [§9](#9-what-still-needs-a-human) lists what a DPO has to rule on before this processing
> should be treated as lawful.

## 0. Where everything is

| What | Where |
| --- | --- |
| Why the regulation requires any of this | [`gdpr-obligations.md`](gdpr-obligations.md) (research, P1T-168) |
| Basis per origin, the notice, the Art. 9 stance | [`gdpr-processing-basis.md`](gdpr-processing-basis.md) |
| How a person comes to own a row | [`expert-claims.md`](expert-claims.md) |
| Pause, the Art. 22 route filter, the visibility seam | [`expert-visibility.md`](expert-visibility.md) |
| Every store holding personal data, and the one erasure path | [`personal-data-and-erasure.md`](personal-data-and-erasure.md) |
| The access view and the export | [`transparency-and-export.md`](transparency-and-export.md) |
| The retention clocks | [`retention.md`](retention.md) |
| The Art. 22 necessity argument and its safeguards | [`art22-safeguards.md`](art22-safeguards.md) |
| The page all of it reaches the person through | [`expert-privacy-page.md`](expert-privacy-page.md) |
| The DPIA | [`dpia-expert-workspace.md`](dpia-expert-workspace.md) |
| The written LIA for staff-created rows | [`legitimate-interest-assessment.md`](legitimate-interest-assessment.md) |

## 1. The one decision everything else hangs off

**Basis is a property of how the record came to exist, not of the person and not of a setting.**

| Origin | Basis | Consequences |
| --- | --- | --- |
| The person registered and claimed the row | **Art. 6(1)(b)** — steps at their own request prior to a contract | Export is a *right*; the row is scanned; no objection row |
| A Service Manager entered them | **Art. 6(1)(f)** — legitimate interest | Export is a *courtesy*; **never scanned**; objection row present, honoured as deletion |
| An agent staged a draft | none yet | Not a person's record until a human promotes it |

This is enforced in the database, not in a service method: `ProcessingRecord` carries a CHECK
constraint pairing origin to basis, so a row with a mismatched pair cannot be written at all, and a
BEFORE UPDATE trigger makes the table append-only. A basis is therefore a historical fact with a
timestamp, not a mutable column somebody can quietly correct later.

**Why this shape rather than a consent checkbox.** The research found the two regulators in direct
conflict — the ICO prohibits consent for holding somebody against future roles, the CNIL recommends
it for a CV pool — and EDPB GL 05/2020 §123 forbids swapping out of consent once chosen. A consent
checkbox is the one option that cannot be corrected if it turns out to be wrong. The route taken is
CNIL's staffing-agency reading (Art. 6(1)(b) pre-contractual measures), which is the most favourable
ground found in the research *and* the only one that preserves Art. 20 portability. Full reasoning:
[`gdpr-processing-basis.md`](gdpr-processing-basis.md) §1.

## 2. Art. 22 is conceded, and the population is narrowed instead

The scan scores every scannable Expert against a job description and ranks them. The research's
sharpest finding was that "a Service Manager approves the proposal" almost certainly does not make
this non-automated: SCHUFA C-634/21 §50 puts the decision at the point where the score plays a
determining role, and the ICO's 2026 field study found employers who believed they were doing
decision support were in practice rubber-stamping ranks.

**So the design does not argue the point. It concedes the automation and relies on Art. 22(2)(a).**

The necessity argument is written out in full in [`art22-safeguards.md`](art22-safeguards.md) §2 —
in short: an Expert registered in order to be placed, assessment *is* the relationship, and at bench
scale the alternative to automated assessment is no assessment. The three Art. 22(3) safeguards are
built on the scan row itself: human intervention (`POST /api/contests` → a review queue), the right
to express a view (`ContestNote`, shown to the reviewer first), and the right to contest (the same
control, reopening after a review).

**The consequence that mattered most is a restriction, not a feature.** 22(2)(a) covers only people
who asked. A staff-created row sits on legitimate interest, which is not one of the three Art. 22(2)
exceptions — so those rows are **excluded from the scan entirely**, by a predicate in the visibility
seam (`RosterVisibility.Scannable()`), not by a flag somebody sets. The ICO's rule that human
involvement must reach *every* candidate is answered by there being no automated decision about that
population at all, rather than by promising review nobody would do.

That is a real product cost — a Service Manager can enter somebody the scan will then never find —
and it is recorded as a cost in [`expert-visibility.md`](expert-visibility.md) §5 rather than
smoothed over.

## 3. Erasure is one path, and a declaration is what keeps it honest

`PersonalDataDeclaration` is a single list of every store holding personal data, what erasure does to
it, and **why** — the reason is a required field, so "it was hard" cannot be a reason. Two readers
consume it: the erasure path, and a test that walks the real EF model transitively through foreign
keys and fails if a store holding personal data is not declared. A future entity cannot be added
without either declaring it or breaking the build.

Three actions, and the shape of each:

- **Delete** — the row goes, almost always by database cascade. A cascade cannot be forgotten by a
  code path that did not know about it, which is the point.
- **Scrub** — the row survives because a human made a decision on it (Art. 17(3)(e)) and its
  personal fields are nulled in place. `StaffingProposalCandidate` keeps `ExpertId` and the scores;
  `StaffingProposal.PackageJson` keeps its structure with six named fields nulled.
- **Keep** — nothing is in this state.

**Scrubbing is pseudonymisation, not anonymisation** (EDPB GL 01/2025 §22). A surviving row with an
`ExpertId` on it is acknowledged personal data under **Art. 18 restriction**. Deleting the `Expert`
row does not launder the residue, and nothing in this repository claims it does — that wording is
load-bearing and deliberate. The `ExpertId` on a scrubbed row is a restricted-processing reference,
which is why it is deliberately *not* a foreign key: the row has to outlive the person.

**Deletion is irreversible and there is no grace window**, because there is no email with which to
undo it. It requires control-word re-authentication and it ends every live session on both hosts —
the account row's absence is what refuses them, so there is no token to revoke.

## 4. Two transparency surfaces, because they owe opposite things

| | Access view (Art. 15) | Export (Art. 20) |
| --- | --- | --- |
| Derived data — scores, bands, rationales, digests, embeddings | **shown, in full** | **absent** |
| Why | Access covers inferred data (EDPB GL 01/2022 §§97, 99) | Portability covers only provided/observed data (WP242 pp. 9–11) |
| Format | A page | JSON — the rendered CV PDF does not qualify |
| Label | — | **a right** under 6(1)(b), **a courtesy** under 6(1)(f) |

The export payload is byte-identical either way. Only the label moves, because under legitimate
interest there is no Art. 20 duty and claiming otherwise would misdescribe what the person is owed.

**Recipients are stated as categories, not as an access log**, and one of them is new information
rather than a restatement: **Google (Gemini) is named as the model provider.** Before this effort the
service disclosed that to nobody. Logging every view by everyone would answer a disclosure duty by
manufacturing a large new store of personal data about access, which would then need its own
disclosure, retention and erasure.

The Art. 15(1)(h) logic text concedes the automation in the same words §2 does, and says so to the
person's face. Its consequence is a constraint worth having: **the rationale has to be defensible,
because its subject reads it.**

## 5. Two clocks, and only the person moves their own

| Population | Period | Anchored to |
| --- | --- | --- |
| Claimed / self-registered | **2 years** | last activity, falling back to collection |
| Unclaimed | **6 months** | collection |

Two years is CNIL's number. Six months for the unclaimed population is a finding, not a softer
version of one: that person was never informed, cannot exercise any right because they do not know
this service exists, and is never scanned anyway. A short clock is the only mitigation actually
available, and it **drains the Art. 14 gap over time** instead of letting it accumulate.

Periods are calendar years and months, not fixed day counts — "two years" crosses a leap day about
half the time, and a promise that expires somebody a day early is a promise not kept. Expiry runs
**the same erasure code** as a person deleting themselves; there is no second deletion path to drift.

Two deliberate refusals: the CNIL 5-year discrimination-defence archive is **declined** (a second
restricted store with its own disclosure, retention and erasure path, for litigation that will not
come), and **the sweep is off unless a deployment turns it on**, so no environment deletes people by
default.

## 6. What the person can actually do, in one place

Every right above is a row on one page (`/me/privacy`), each with its action at the end of its own
row: read the access view, download the export, pause, object, contest a score, delete everything.
Two properties of that page are load-bearing and documented as such in
[`expert-privacy-page.md`](expert-privacy-page.md): one source of truth about state, and the
distance between pause and delete *is* the separation between them.

**Objection is not adjudicated.** Nobody weighs the company's interest against the person's; there
is no flow in which somebody decides. Art. 21 objection deletes the record. It still asks for the
control word, because "unconditional" describes the outcome, not the proof.

## 7. Where the build diverged from the plan

The acceptance criterion for this file is that it matches the code as built. These are the places
where it does not match the plan as written.

| Plan said | What shipped | Why |
| --- | --- | --- |
| P1T-172's store table: keep `ProcessingRecord` after erasure | **Deleted** with the person | Keeping rows proving we once had a basis for data we no longer hold is not something Art. 17(3) plainly covers. The table's BEFORE UPDATE trigger also makes it delete-or-nothing |
| P1T-171: a consent decision | **No consent anywhere**; basis per origin | Consent is the one choice that cannot be corrected later (GL 05/2020 §123), and the two regulators disagree about it |
| P1T-179 framing: argue meaningful human review | **Automation conceded**, 22(2)(a) relied on, LI rows excluded from the scan | The ICO's 2026 study says the review would not be believed, and it would not have been true |
| P1T-183 planned the transparency notice as the Art. 15 surface | **Two artefacts**: a versioned notice somebody acknowledged, and `Art15Disclosure` describing the service as it is now | Versioning "how things are today" answers the wrong question |
| P1T-175 prototype's Object button asked for nothing | **Asks for the control word** | The prototype had no backend; the act is irreversible and the control word is the only proof available |
| P1T-184 planned `OwnerUserId` on `ExpertDetailDto` | A separate `GET /api/claims/ownership/{expertId}` | Adding the fields breached the tool's cost ratchet by 9 tokens; the ratchet only moves down |
| P1T-168 said the research markdown lands with this slice | It does — landed **verbatim**, with a note | Editing a pre-build research record to agree with the build destroys the only thing it is good for |

One more, smaller and worth knowing: a foreign row is **404, never 403**. Telling somebody a record
exists but is not theirs is itself a disclosure about that record.

## 8. What holds all of it

Documents drift; these do not, because they fail the build.

- `PersonalDataDeclarationTests` — walks the real EF model transitively through FKs; an undeclared
  store holding personal data fails.
- `OwnershipScopeCoverageTests` — reflects over every `I*Service` in the Application assembly;
  exemptions need a written reason, and an unrecognised parameter throws rather than skipping.
- `VisibilitySeamTests` — a source sweep; `HiddenAt` may appear only in an allow-listed set of files,
  so the pause cannot be re-implemented somewhere else.
- `EndpointClassificationTests` — walks the live `EndpointDataSource`; a new endpoint must be
  classified.
- `frozenHooks.test.ts` — a frozen `data-testid` inventory, asserted in both directions.
- The append-only trigger and the origin↔basis CHECK constraint — the two rules that are the
  database's, not the application's.

## 9. What still needs a human

Nothing below is settled by anything in this repository, and the DPIA repeats them as residual risk.

1. **Art. 14 for staff-created rows.** A real person a Service Manager enters must be informed within
   a month, and this service has no way to reach them. They learn we hold them only if they happen
   to register and claim the row. Mitigated **only** by the 6-month clock, which drains the
   population rather than discharging the duty. **This is an unresolved breach, not a managed risk.**
2. **Art. 13 on change.** An Expert who never signs in again never sees an updated notice. Same
   cause, same non-solution.
3. **Art. 9 free text.** Whether unfiltered CV prose plus semantic retrieval is special-category
   processing at all is genuinely unsettled (C-184/20 §§122–128). The response is minimisation plus a
   standing prohibition on inference — **a mitigation, not a solution.** Nothing stops somebody
   writing health or union detail into a summary, and nothing removes it once written.
4. **Whether the necessity argument in §2 actually holds.** It is this team's argument. It has not
   been reviewed by anybody qualified to say it survives.
5. **Whether being passed over by the scan is a "similarly significant effect".** WP251 draws no
   line for an internal bench.
6. **The two-year and six-month periods.** CNIL's number and a reasoned inference respectively.
   Neither is a legal determination.
7. **Prior consultation under Art. 36**, if the DPIA's residual risk is read as high. Not assessed
   here.

Both notice gaps in (1) and (2) close immediately if email is ever added — which the P1T-167 map
rules out permanently. That is a product decision with a compliance cost, and it should be re-taken
with that cost in view rather than inherited.
