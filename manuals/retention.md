# How long we keep somebody, and what makes the clock move

> **Status (2026-09-02):** shipped as P1T-188. `Expert.LastActivityAt` exists, `RetentionPolicy` is
> the one function that decides expiry, and `RetentionWorker` sweeps — **disabled by default**.
> Decision: [P1T-180](https://linear.app/p1t0ns-nest/issue/P1T-180). Erasure:
> `manuals/personal-data-and-erasure.md`.

## 1. Two anchors, because `Expert` had no dates at all

The row carried no `CreatedAt` and no `UpdatedAt`, so a retention clock had nothing to measure from.

- **Collection** = the first `ProcessingRecord`. It already existed, it is append-only and
  per-record, and it is exactly Art. 5(1)(e)'s reference point. For an unclaimed record it is the
  only date in the system.
- **Last activity** = `Expert.LastActivityAt`, null until the person does something themselves —
  which is the permanent state of a record nobody has claimed.

## 2. Only the person moves their own clock

Staff edits and agent scoring deliberately do **not**. This is the rule the whole slice turns on: if
being scored counted as contact, a bench running weekly scans would never expire anybody — the
service would keep people alive by looking at them, which is the exact inverse of what retention is
for.

It is enforced by `ExpertActivityInterceptor`, one `SaveChangesInterceptor` registered **only in the
Web host**, which reads the ownership scope that already exists:

| Caller | Scope resolves to | Clock moves? |
| --- | --- | --- |
| The Expert, on their own record | `OwnedBy(their row)` | **yes** |
| A Service Manager | `Unrestricted` | no |
| Any agent (MCP host) | `Unrestricted` | no |

An interceptor rather than a line in each service, because the rule is a *negative* one and a stamp
written into eleven controllers is a rule the twelfth forgets. Here a new write path is covered the
day it is written. `RetentionTests.Neither_a_staff_edit_nor_agent_scoring_moves_the_clock` asserts
the negative, and `The_experts_own_write_moves_the_clock` the positive — the negative alone would
pass by never stamping at all.

> Pausing counts as activity. Somebody who pauses is present and choosing; the pause is an exit
> control, not an absence.

## 3. Two periods, and why they differ

| Population | Period | From |
| --- | --- | --- |
| Claimed / self-registered | **2 years** | last activity (falling back to collection) |
| Unclaimed | **6 months** | collection |

Two years is CNIL's number, taken because the transparency design put this service on the EU/CNIL
reading. Six months for the unclaimed population is the finding rather than a softer version of it.
An unclaimed record is held on legitimate interest, so it is **never scanned**; its subject was
**never informed**, because this service sends no email and never will; and they can exercise no
right at all, because they do not know we exist. A short clock is the only mitigation actually
available, and it **drains that gap over time** instead of letting it accumulate.

**Consequence, stated:** a record a Service Manager enters and nobody claims disappears in six
months. It was invisible to the scan for that entire period anyway, so nothing that was working is
lost.

Both are counted in **calendar** years and months, not fixed spans of days — "two years" crosses a
leap day about half the time, and a promise that quietly expires somebody a day early is a promise
not kept. A test caught that; the first draft used `TimeSpan.FromDays(365 * 2)`.

The 5-year discrimination-defence archive is **consciously declined**: a second restricted store
with its own disclosure, retention and erasure path, for litigation that will not come.

## 4. Expiry runs the erasure path — the same code

Retention is a **trigger**, not a second mechanism. Two implementations of "delete a person" will
diverge; it is only a question of when.

The obstacle was real: `EraseMineAsync(actingUserId, controlWord)` is deliberately shaped so the API
*cannot* express erasing somebody else, and an expiry has no acting user, no control word, and — for
an unclaimed record — no account at all. The resolution is a shared private core with two entry
points on **two interfaces**:

- `IErasureService.EraseMineAsync` — the person asking. Gated, unchanged, still cannot name anybody
  but the caller.
- `IRetentionErasure.EraseExpiredAsync(expertId)` — the clock. Nothing in the Web API routes to it.

`RetentionTests.Expiry_and_a_requested_deletion_leave_the_database_in_the_same_state` builds two
identical people, removes one each way, and compares the residue. That assertion is what keeps the
two from drifting.

## 5. The demo trap

Without an exclusion the demo roster silently evaporates and every developer's local environment
empties itself overnight. Nothing in the schema distinguishes seeded rows from real ones.

The rule chosen is **RFC 2606 / RFC 6761 reserved domains** — `example.com`, `.test`, `.invalid`,
`.localhost` and friends. No real person has an address there, so a record carrying one is
fabricated and retention does not apply to it. This is a rule rather than a heuristic, and it holds
in production as well as locally.

Two alternatives were rejected: matching invented surnames (brittle, and faintly absurd), and a
database column marking demo rows — a schema change that would then need its own Art. 15 disclosure
entry, describing data that is not about anybody.

A record with **no** address is also left alone: that is an agent-staged draft, and the promote gate
decides a draft's fate, not a clock.

## 6. The sweep is off unless a deployment turns it on

`Retention:Enabled` defaults to **false**. For the one background job whose normal operation
destroys somebody's data, the safe default is "not running": a developer who pulls the branch, seeds
a roster and leaves the app open overnight should not come back to an empty database. Switching it
on in production is also the moment somebody reads what it does.

It lives in the **Web host**, which had no `BackgroundService` before, because that is where the
erasure path is registered — erasure depends on the control-word hasher, a Web type. Giving the MCP
or Agents host its own way to delete people would be exactly the divergence §4 exists to prevent.

Shaped after `ReconcileWorker`: an explicit enabled flag, one scope per pass, and it never lets a
failure take the host down. A deletion whose deadline was months ago can wait for the next tick.
Every pass that expires anything logs it — "it went quiet" must never be the only evidence it ran.

## 7. What the person is told

The access view (P1T-187) carries the period **and their own expiry date** — Art. 15(1)(d) asks for
the period, and the date is the form of it somebody can act on. Inside the final thirty days
`ExpiryBanner` renders on the workspace.

There is a property worth naming: **reading the warning is itself activity, so for an owned record
the banner cures the thing it warns about.** It says so, because a warning that quietly fixed itself
would leave somebody thinking they still had to act. An unclaimed record's reader gets different
words: nothing passive saves it, and claiming it is what would.

Somebody who never signs in never sees any of it. That gap is real, unsolvable here, and the same
one the transparency notice has.
