# Automated scoring: the necessity argument, and the safeguards it obliges

> **Status (2026-09-02):** shipped as P1T-189. The contest control is on the Expert's workspace, the
> queue is on the Users page beside claim approvals, and the outcome is recorded on the scan row.
> Decision: [P1T-179](https://linear.app/p1t0ns-nest/issue/P1T-179). Lawful basis:
> `manuals/gdpr-processing-basis.md`. What is scanned at all: `manuals/expert-visibility.md`.

## 1. We concede the automation

The Roster Scan produces a score, a band and a written rationale for each Expert, persists them, and
ranks people by them. The ranking decides who a Service Manager is shown first, and in practice that
decides who is considered.

**We do not argue that a human is meaningfully in the loop.** That argument was available and it was
rejected: it is the losing game the ICO's field work documents, where employers believe they are
doing decision support and in practice are not. A claim that depends on manager behaviour is a claim
we cannot keep; a stated exception depends on a written argument we control.

So Art. 22 is engaged, and we rely on **Art. 22(2)(a)** — necessary for entering into or performing a
contract.

## 2. The necessity argument, written down

This is the load-bearing legal claim of the whole product, and it belongs in the repository rather
than only in a ticket.

> An Expert is on this bench in order to be placed on Jobs. Placing them requires assessing whether
> they fit a particular Job — that assessment is not incidental to the relationship, it *is* the
> relationship. A Service Manager brings in a job description and needs to know which of the people
> on the bench are worth putting forward.
>
> At bench scale that assessment is automated. Reading every CV against every job description by
> hand is not a slower version of what this service does; it is a different service, one that does
> not exist. The alternative to automated assessment here is no assessment, which is to say no
> placement — the thing the Expert asked for by registering.
>
> The processing is therefore necessary for steps taken at the data subject's own request prior to
> a contract, which is the ground the record already sits on (Art. 6(1)(b), CNIL's staffing-agency
> reading), and the automated decision-making it involves falls under Art. 22(2)(a).

Two limits on that argument, stated so nobody has to rediscover them:

- **It only covers people who asked.** A record a Service Manager created sits on legitimate
  interest, which is not among the three Art. 22(2) exceptions — so those records are excluded from
  the scan entirely (P1T-185). The argument above does not stretch to somebody who never asked for
  anything.
- **It is an argument, not a licence.** It justifies scoring people to place them. It would not
  justify scoring them for something else, and a future feature that scores an Expert for a purpose
  other than a Job needs its own basis rather than a share of this one.

## 3. What relying on 22(2)(a) obliges

Art. 22(3) makes three safeguards mandatory. All three land on the scan row, because that row *is*
the decision.

| Safeguard | Where it is |
| --- | --- |
| Human intervention | `POST /api/contests` → the queue → a Service Manager reviews |
| The right to express a view | `ContestNote` — the person's own words, shown to the reviewer first |
| The right to contest | the same control; asking again after a review reopens it |

And the precondition: **you can only contest what you can see.** The score, the band and the
rationale written about somebody are shown to them in full (P1T-187), which is what makes the
control meaningful rather than decorative.

> The consequence is a feature, not a cost: **the rationale has to be defensible, because its
> subject reads it.** That is a healthier constraint on what a model may write about a person than
> secrecy would be.

## 4. What this deliberately is not

**Not an appeals workflow. Not an SLA.** There are no states, no deadlines, no escalation. What is
owed is that a person can ask, say why, and have a human's conclusion recorded — and machinery
beyond that is machinery nobody maintains.

A review records `upheld` or `overturned` and the reviewer's own words back. *Overturned* rewrites
nothing: a scan row is a working artefact, and what changes is that a person now decides this
candidate by hand.

**No override-rate, rank-departure or time-on-proposal instrumentation** — and this is a deliberate
reversal, not an omission. All of that existed to evidence "not solely automated", a claim §1
abandons. Conceding the automation is precisely what removes the need to measure human behaviour.

> **If we ever stop relying on Art. 22(2)(a), that trade must be re-examined.** Whoever changes the
> basis inherits the measurement burden this slice discarded.

## 5. Erasure wins

A pending contest is a request *by* somebody. If they erase themselves, the request goes with the
scan row — the queue simply loses the item. Correct, not lossy, and it needed no code: the scan row
is deleted whole by the erasure path (P1T-186). `ContestTests.Erasure_takes_a_pending_contest_with_it`
pins it, because "it happens to fall out" is exactly the kind of property that stops falling out.

## 6. One page, two lists

The contest queue sits on the Users page beside the claim approvals — one place a Service Manager
goes for the decisions only a person can make. It is two lists rather than one merged queue: a claim
and a contested score need different columns and different verbs, and merging them would make both
harder to read.

Each list says on screen why it matters. For claims, that a matching email proves nothing. For
contests, that reading what the person wrote **is** the safeguard making the scoring lawful, and not
a formality.
