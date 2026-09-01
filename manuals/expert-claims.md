# Claims: how a person comes to own a roster row

> **Status (2026-09-01):** shipped as P1T-184. `PendingClaim` and `ClaimCode` are persisted,
> registration matching runs at signup, approval and revocation append `ProcessingRecord` rows, and
> `Expert.Email` is Service-Manager-only after registration.
> Decision: [P1T-173](https://linear.app/p1t0ns-nest/issue/P1T-173). Lawful basis:
> `manuals/gdpr-processing-basis.md`. Ownership scope:
> [P1T-182](https://linear.app/p1t0ns-nest/issue/P1T-182).

## 1. The problem, stated plainly

`User.Email` is **never verified** and **this service sends no mail, ever** — not at signup, not for
recovery, not for an Art. 14 notice. That is a fixed constraint of the map, not a gap waiting on a
mail provider.

So the obvious design — "your email matches this bench row, here is your CV" — hands anybody who
knows a colleague's address that colleague's CV. Everything below follows from refusing that.

**A matching address is evidence of nothing.** The system's job is to stop pretending otherwise, in
the data model and on the screen alike.

## 2. What registration does

`AuthController.SignupComplete` creates the account, then calls
`IClaimService.BindOnRegistrationAsync`. Matching is **case-insensitive** and runs against
**non-`Draft` rows only**.

| Rows matched | What happens | `RegistrationBinding` |
| --- | --- | --- |
| none | a fresh row, `Active`, owned on the spot, origin `SelfRegistered` | `OwnsNewRow` |
| exactly one, unowned | a `PendingClaim` for a Service Manager; **nothing is granted** | `ClaimPending` |
| anything else | **no claim on any row**, a flag raised for a Service Manager | `AmbiguousRaised` |

"Anything else" is two shapes with one answer: more than one row matched, or the single match
already belongs to somebody. Neither may be resolved automatically. Auto-picking between duplicates
hands one person another person's CV on a coin flip, and binding a row that already has an owner is
the takeover the whole design exists to prevent.

**Drafts are excluded on purpose.** A draft is agent-staged from a resume and no human has looked at
it; claiming one hands over a row nobody has vetted. Drafts are also exempt from the roster's email
uniqueness, so they are invisible here twice over.

> **The row a fresh registration creates has no name.** `FirstName` and `LastName` are left empty
> rather than guessed from the address. It is a bench row about a real person, and inventing their
> name is worse than showing an incomplete row they fill in themselves.

### The duplicate case is currently unreachable, and the rule stays anyway

`IX_Experts_Email` is unique across `Active` rows, and drafts do not match — so Postgres cannot
currently hold two matchable rows with one address. The rule is kept, and tested at the Application
layer, because what makes it unreachable is *one index filter*. P1T-185 adds a hidden state that
deliberately keeps rows `Active`; a matching rule that quietly starts guessing the day that filter
moves is not a rule. The reachable half — a match on a row somebody already owns — is asserted
against Postgres in `Web.Tests/ClaimBoundaryTests`.

## 3. A pending claim grants nothing, and looks like nothing

There is no guard for this and there is not meant to be one. An account with no owned row resolves
to `OwnershipScope.OwnedBy(null)` (P1T-182), so every own-row endpoint 404s for it — identically for
"claim pending", "claim rejected" and "never claimed anything". The test asserts the **sameness of
the responses**, not merely that they are refusals: a difference between pending and not-yours is a
way to probe the roster for who is on it.

## 4. The claim record is an entity, and it is kept

`PendingClaim`: claimant, the address that matched, target row, match count, state, created,
decided-by, decided-at. Rows are **kept after resolution**, never deleted.

A state flag on the `Expert` cannot express "rejected, then claimed again by somebody else", and
that is exactly the sequence an audit asks about. `ClaimantEmail` is snapshotted rather than read
through the FK, because a Service Manager may change the account's address afterwards and the
approver's screen must show what was actually matched on.

`ExpertId` is **nullable**, and a null target is not a claim on anything — it is the raised flag
itself (`ClaimState.Ambiguous`). One queue, one table, one place to look.

Two partial unique indexes make "at most one open claim" database truth: one per claimant, one per
row, both filtered on `State = 'Pending'` so resolved rows can pile up forever.

## 5. Approval, and what the approver is told

Any Service Manager, on the existing Users page — an account-shaped decision, sharing the page with
the Art. 22 contest queue (P1T-189).

The screen states, above the table, that **a matching email address proves nothing**
(`CLAIM_EVIDENCE_WARNING`, asserted by a test — it is a design requirement, not decoration). An
approval UI that looks authoritative invites rubber-stamping, which is the failure the ICO
documented in the scoring context. The confirmation repeats the consequence: the claimant will read
and edit that record, and it becomes scannable.

Approval binds `OwnerUserId` and appends a `ProcessingRecord` — `SelfRegistered` / 6(1)(b), carrying
**the notice version the claimant acknowledged at registration**. Both land in one transaction: a
row that is owned while still recorded on legitimate interest is a compliance defect for as long as
it lasts.

## 6. Claim codes: the only real proof available

A Service Manager generates a single-use code from the expert's page and hands it over out of band —
in person, by phone, whatever channel they already use. **Never by email**, which is the thing this
mechanism replaces. Redeeming binds ownership with **no approval step, because the code is the
proof**.

- 160 bits of randomness, Crockford-style alphabet without `I`, `L`, `O`, `U` — it gets read aloud.
- Stored as **SHA-256 of the normalised code**. The plaintext is shown once and never again; a
  bearer secret readable out of the database would be a second way to take over a CV. A password
  hash would be theatre here: there is nothing to guess in 160 random bits.
- Case and the grouping dashes are normalised away. Refusing a correct code because it was typed in
  lower case sends somebody back to the Service Manager for nothing.
- Single-use is a `RedeemedAt` stamp, not a delete, so a replay is a fact somebody can see. A replay
  and a code that never existed are refused with **the same words** — a redemption endpoint must not
  confirm which guesses were once real.

## 7. Revocation appends, and the button says what it does

Service-Manager-only. It clears `OwnerUserId` and appends a `ProcessingRecord` returning the row to
`StaffCreated` / legitimate interest. It **never rewrites** the earlier record: the history has to
keep showing the row *was* on 6(1)(b), because it was scannable in that window (EDPB GL 05/2020
§123).

The consequence chains, so the dialog spells it out (`REVOKE_CONSEQUENCE`, asserted by a test):

> unclaimed → legitimate interest → **no longer scanned for Jobs** → this person stops being
> considered.

That is correct behaviour and a genuinely destructive act, and an approver who is not told will
reach for this button to tidy up.

## 8. Email is immutable to the person who benefits from changing it

`Expert.Email` is settable at registration and **Service-Manager-only** thereafter, enforced in
`ExpertService` (`FrozenEmailAsync`) rather than at an endpoint, because the Web API and the MCP
server share these services.

This is a security fix, not a UX limitation. The address does three jobs at once — login identifier,
claim key, CV contact — with no verification behind any of them. An owner who could edit it could
point their row at a bench member's address and re-trigger matching, reaching the takeover the
pending-claim design prevents through the my-account door instead.

Two details, both deliberate: a real change is **refused loudly** (400, naming who can do it) rather
than silently ignored, because somebody who tried needs to know it did not happen; and a
**case-only** difference is neither a change nor an error — the stored value simply stands.

## 9. Unowned is legitimate, permanent, and visibly degraded

Seeded rows, the demo roster and anything staff create start unowned and may stay that way. That is
a real state, not a backlog someone forgot to drain.

But it is not a neutral one: unowned means legitimate interest, and LI carries no Art. 22(2) route,
so **an unclaimed bench member is not scanned and therefore not considered** (P1T-179, enforced by
P1T-185). The expert's page says so in those words. That gives staff a working reason to get people
to claim their rows, which is a better forcing function than a policy nobody reads.

## 10. Two things this slice does not do

- **Reaching the person.** Art. 14 says a Service Manager who enters a real person must inform them
  within a month. We cannot. A claim code closes it for one person at a time, in person, and nothing
  closes it in general. Recorded in `manuals/gdpr-processing-basis.md` §7 as well, because it is the
  same gap seen from two directions.
- **The Expert's own claim status.** The workspace shows the redemption field and nothing else about
  a pending claim, because a session that owns no row is deliberately indistinguishable from one
  whose claim is waiting. Telling the person which they are is P1T-190's job, from the registration
  response (`AuthSessionResponse.rosterBinding`) rather than from a lookup that would undo the
  property.
