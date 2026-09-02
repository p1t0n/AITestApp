# Legitimate interest assessment: Experts a Service Manager entered

> **Status (2026-09-02):** first version, shipped as P1T-192. Covers **one cohort**: roster rows
> created by a Service Manager rather than by the person themselves — the population whose
> `ProcessingRecord` pairs origin `StaffCreated` with basis **Art. 6(1)(f)**.
>
> **This is a design input requiring a human sign-off. It is not legal advice.** The ICO expects a
> legitimate interests assessment in writing, and Art. 5(2) requires it as an accountability record;
> this is that record, written by the engineering team so somebody qualified has a concrete argument
> to accept or reject. **It is not complete until the sign-off block at the foot is filled in.**
>
> One assessment covers the cohort — the processing is identical for every row in it, which is the
> condition under which the ICO accepts a cohort-level LIA rather than one per person.

## Who this is about, and who it is not

| Cohort | Basis | Covered here |
| --- | --- | --- |
| The person registered and claimed their row | Art. 6(1)(b) | **No.** Steps at their own request prior to a contract; no LIA needed |
| **A Service Manager entered them** | **Art. 6(1)(f)** | **Yes** |
| An agent staged a draft nobody promoted | none | No. Not a person's record until a human promotes it, and the promote gate is where a basis is written |

A row moves out of this cohort permanently when the person registers and claims it: a new
`ProcessingRecord` is appended, the basis becomes 6(1)(b), and this assessment stops applying to
them. The chain is append-only, so the fact that they were once held on legitimate interest remains
visible — which is what lets anybody check that the transition happened rather than being asserted.

**Scale, honestly:** this cohort exists because a bench is built before it is populated with
volunteers. It is expected to shrink as people claim their rows and as the 6-month clock removes
those who never do. It is not a growth population by design.

## Part 1 — The purpose test

### What is the interest?

Being able to put forward the people this company already works with, without requiring each of them
to self-register before they can be considered for a client's job.

Concretely: a Service Manager knows a contractor's history, a client brings a job description, and
the company needs the roster to contain that person in order to consider them. Requiring
self-registration first means the bench is empty exactly when it needs to be useful, and the people
concerned are typically already in a working relationship with this company.

### Is it legitimate?

It is a **commercial interest of the controller**, which Recital 47 expressly contemplates, and
staffing is the company's actual business rather than a secondary use of data collected for
something else. It is also, in part, the **interest of the person**: being on the bench is how they
get placed, and someone left off it is not considered.

It is **not** a third-party interest dressed up as ours, and it is not a public-interest claim.

### Who benefits, and how much?

| Party | Benefit |
| --- | --- |
| The company | The bench is usable from the start; existing contractors can be considered |
| The client | A shortlist drawn from people the company actually knows |
| **The person** | Real but **unrequested** and, until they claim the row, **unknown to them.** This asymmetry is the whole difficulty, and it is not smoothed over below |

### Would not doing it matter?

Yes, but not catastrophically. Without this cohort the roster contains only self-registrants, and the
company falls back to considering people outside the system — by memory, by spreadsheet, by asking
around. That fallback is worse for the person in every respect except one: it does not create a
record they were never told about. **That single exception is the strongest argument against this
processing, and it is taken seriously in Part 3.**

## Part 2 — The necessity test

### Is the processing necessary for that interest?

Holding the record is necessary: the interest is precisely to have the person in the roster.

**But only the record.** The necessity test is what removes everything else, and the design applies
it as an exclusion rather than as a preference:

| Processing | Necessary for this interest? | What the design does |
| --- | --- | --- |
| Storing identity, career history, skills, availability | Yes | Held |
| Embedding the career narrative for retrieval | Yes — a roster nobody can search is not a usable roster | Held |
| **Automated scoring and ranking against a job (the scan)** | **Question does not arise** | **Excluded entirely.** Art. 22(2)(a) is unavailable to a 6(1)(f) row, so the exclusion is a legal requirement, not a proportionality choice |
| Appearing in an Art. 22 route at all | No | Excluded by the same predicate in the visibility seam |
| Indefinite retention | **No** | 6 months from collection |
| Sending the data to a client | Only once a human has decided to put them forward | A proposal is a human act, not an automatic consequence of being on the bench |

The scan exclusion is worth stating as a compliance property rather than a side effect: **a
staff-created row is never automatically assessed, never ranked, and never scored.** It is enforced
by `RosterVisibility.Scannable()` in the query, and by a source sweep that keeps the seam single.

### Is there a less intrusive way?

Three alternatives were considered.

**Ask the person first, and only enter them once they agree.** This is the honest alternative and it
is what should happen wherever it can. It is not available in the general case for the reason that
drives everything else here: **this service has no email**, so there is no channel through which to
ask somebody who is not already signed in. This is a self-inflicted constraint, not a law of nature
— see the note at the end of Part 3.

**Consent instead of legitimate interest.** Unavailable, and not merely inadvisable. Consent must be
freely given, specific and informed by the person; you cannot obtain it from somebody you cannot
contact. The ICO also prohibits consent for holding somebody against future roles, and EDPB GL
05/2020 §123 forbids swapping out of consent later if it proves invalid — so choosing it here would
be both impossible and unfixable.

**Hold less about them — a name and a contact detail only, filling in the rest on claim.** Genuinely
less intrusive, and genuinely rejected: a row with no career history cannot be matched against a job
description, so the row would not serve the interest that justifies holding it. Holding a *useless*
record about somebody is not a lesser intrusion, it is a pointless one.

## Part 3 — The balancing test

### Reasonable expectations

**Mixed, and weaker than is comfortable.** A contractor who has worked with this company would not
be surprised that it keeps a record of their skills and availability — that is ordinary in staffing,
and Recital 47 treats the relationship between controller and subject as central to expectation.

What they would **not** reasonably expect:

- That the record exists in a system they have never been told about.
- That their career narrative was sent to **Google's Gemini** models to be embedded.

The second is disclosed to the person in the notice and on the access view — **but only if they ever
reach it**, and by definition the members of this cohort have not. Disclosure that never arrives is
not disclosure. This weighs against the processing and is not counted as a mitigation.

### Nature of the data

Ordinary professional data — career history, skills, availability, contact details. **No special
category data is sought**, and none should be present; the risk that some arrives incidentally in
free text is real and is treated in [`dpia-expert-workspace.md`](dpia-expert-workspace.md) §5. No
financial data, no health data, no data about children, no criminal-offence data.

Not trivial, though. A `Qualification` names an institution and a credential number, and career
history is identifying well beyond this roster.

### Likely impact on the person

| Impact | Assessment |
| --- | --- |
| Being scored or ranked by software | **None.** Excluded from the scan by law and by code |
| Being put in front of a client without knowing | Possible — but only after a Service Manager decides to, which is the same act as any other referral |
| Loss of control over their own data | **Real, and the core harm.** Until they claim the row they cannot read it, correct it, pause it, export it, object to it or delete it, because they do not know it exists |
| Distress on discovering the record | Plausible. Discovering that an employer holds your career history and has sent it to a model provider, without ever being asked, is a reasonable thing to be unhappy about |
| Financial or discriminatory harm | Low. No scoring, no inference of protected characteristics, and rankings do not exist for this cohort |

The severity of the core harm is bounded by two facts: the record contains what the person
themselves would have entered, and it is not automatically acted on by software.

### Safeguards, and which ones actually count

| Safeguard | Does it weigh in the balance? |
| --- | --- |
| **Excluded from the scan and every Art. 22 route** | **Yes, heavily.** Removes the highest-severity harm outright |
| **6 months from collection, then deleted by the same erasure path a person's own deletion uses** | **Yes.** The single most important mitigation for the unclaimed population: the exposure has a hard end date that requires nobody to remember it |
| **Art. 21 objection honoured unconditionally as deletion, no adjudication** | **Yes** — but only reachable after they claim the row |
| The row is visibly degraded until claimed, so its status is never ambiguous to staff | Yes, modestly |
| No inference of protected characteristics, ever | Yes |
| Erasure declared per store and enforced by a test over the real model | Yes |
| Basis and origin recorded per row, append-only, constraint-enforced | Yes — accountability, and it makes the transition to 6(1)(b) checkable |
| The transparency notice covering all of this | **Barely.** It is complete and it is accurate, and this cohort has not seen it |

### The Art. 14 duty this does not discharge

Art. 14 requires that a person whose data was not obtained from them be informed **within one
month**. For this cohort, **that does not happen.** They learn of the record only if they
independently register and claim it.

This is recorded as an **unresolved breach, not a managed risk**. The 6-month clock is the only
mitigation actually available: it drains the population rather than discharging the duty, and a
duty that expires is not a duty performed.

**Everything in this assessment is downstream of one product decision: this service sends no email.**
That decision creates the interest (you cannot ask people who cannot be reached), rules out the
better alternative (asking first), and causes the breach (you cannot inform them either). **It closes
immediately if email is ever added** — which the P1T-167 map rules out permanently. That trade should
be re-taken deliberately with this cost in view, rather than inherited from a map that was drawn
before the cost was known.

### Outcome

**On the interest and necessity tests, the processing is justified**, and the exclusions the necessity
test forces — no scan, no Art. 22 route, six months — are built rather than promised.

**On the balancing test, the outcome is conditional and this team cannot settle it.** The safeguards
are real and the highest-severity harm is removed. What remains is a person who cannot exercise any
right because they do not know the record exists, and an Art. 14 duty that is not performed. Whether
a short clock and a narrow processing scope are enough to make that proportionate is a controller's
judgement, not an engineering one, and this assessment does not resolve it in the company's favour.

**Recommendation to the reviewer:** if the balance is judged too fine, the lever is not more
safeguards. It is either **email** — which discharges Art. 14 and reopens the option of asking first
— or a **shorter clock**.

## Review triggers

- Email is added in any form. **Re-do this assessment; the necessity test changes.**
- The cohort stops shrinking, or starts growing as a routine way of populating the bench.
- Any proposal to bring 6(1)(f) rows into the scan, or into any automated assessment. Such a
  proposal needs its own basis; it cannot borrow this one.
- The retention period changes.
- Special-category data is found in the cohort's free text in practice, rather than in theory.

## Sign-off — required, and not yet given

| | |
| --- | --- |
| Prepared by | The engineering team, as part of P1T-192 |
| Date prepared | 2026-09-02 |
| Cohort | Roster rows with origin `StaffCreated`, basis Art. 6(1)(f) |
| DPO / counsel reviewed | **Not yet — required** |
| Controller sign-off on the balancing outcome | **Not yet — required.** Part 3 is deliberately left unresolved |
| Art. 14 breach acknowledged at controller level | **Not yet — required** |

Related: [`expert-workspace-compliance.md`](expert-workspace-compliance.md) (what shipped),
[`dpia-expert-workspace.md`](dpia-expert-workspace.md) (the DPIA, whose R1 is this document's Art. 14
gap), [`gdpr-processing-basis.md`](gdpr-processing-basis.md) (how basis per origin is enforced),
[`gdpr-obligations.md`](gdpr-obligations.md) (the research, verdict 20 on the written-LIA duty).
