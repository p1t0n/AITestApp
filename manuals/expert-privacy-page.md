# Privacy and data: the page, and why it is shaped like that

> **Status (2026-09-02):** shipped as P1T-191, as **Variant A, "The record"** — chosen by the
> P1T-175 prototype run and rewritten properly here. Decisions:
> [P1T-171](https://linear.app/p1t0ns-nest/issue/P1T-171),
> [P1T-172](https://linear.app/p1t0ns-nest/issue/P1T-172),
> [P1T-174](https://linear.app/p1t0ns-nest/issue/P1T-174),
> [P1T-179](https://linear.app/p1t0ns-nest/issue/P1T-179),
> [P1T-180](https://linear.app/p1t0ns-nest/issue/P1T-180). Backing slices: P1T-185 to P1T-189.

## 1. Two properties are load-bearing

Everything else here is detail. These two are why the page looks the way it does, and both were
settled by looking at three built variants rather than by argument.

**One source of truth about state.** Every fact about the state is prose in one column. There is no
status card, no sidebar, no sticky summary — and there must not be. Variant B had one, and on first
contact it claimed *"Visible to Service Managers"* while offering *Pause* and *Download my data*,
next to its own banner saying nothing was held yet. A persistent status surface drifts out of
agreement with the page unless every control in it is state-derived, and there are five states plus
their combinations to keep in step.

**The distance between pause and delete is the separation.** The page is long. The length is the
mechanism: P1T-171 chose two separate controls precisely so nobody deletes when they meant to pause,
and this service has no email to undo it with. **Do not shorten the page in a way that brings the
two controls near each other** — no accordions collapsing the body, no moving delete into a toolbar.
`PrivacyDataPage.test.tsx` asserts the order, because "tidying" is exactly the change that would
silently undo it.

**The accepted cost:** this page tells you your state only if you read the opening sentence. That is
the same property as the first one, not a defect. If it proves insufficient in real use it is a
follow-up ticket, not a redesign.

## 2. The shape

A single column. A state sentence at the top, then `What we hold about you` as a run of labelled
rows in a definition-list rhythm — each right's action as a button at the end of its own row — and
`Deleting everything` at the foot, below a rule, under its own heading, with the control word
inline.

| Row | What it carries | From |
| --- | --- | --- |
| Your CV | what is in it, and a link to edit it | access view |
| Everything in it | the data categories, itemised | Art. 15(1)(b) |
| The search index | that embeddings of their text exist | P1T-187 |
| Assessments | their scores, bands, rationales, match answers — **and a contest button per row** | P1T-189 |
| What we use it for | purposes | Art. 15(1)(a) |
| Who sees it | recipient categories, **including Google (Gemini) by name** | Art. 15(1)(c) |
| How the scoring works | the Art. 22 logic text, rendered as markdown | Art. 15(1)(h) |
| Why we may hold it | the basis, and the source where they did not give it to us | Art. 15(1)(g) |
| How long we keep it | the criterion **and their own expiry date** | P1T-188 |
| A copy of your data | the export, labelled a right or a courtesy | Art. 20 |
| Being offered for work | pause / resume | P1T-185 |
| Objecting to us holding it | LI records only | Art. 21 |
| Complaining about any of this | the supervisory-authority right | Art. 15(1)(f) |

**The access view's `rights` array is deliberately not rendered as a list.** Each of those rights
*is* a row on this page, actionable where it is described. A bulleted restatement above them would
be a second surface saying the same thing — the thing §1 exists to prevent.

## 3. Objecting is honoured unconditionally, and still asks for the control word

Art. 21 objection is **not adjudicated**: nobody weighs the company's interest against the person's,
and there is no flow in which somebody decides. Objecting deletes the record.

Which is exactly why it asks for the control word. "Unconditional" describes the *outcome* — we do
not refuse and we do not argue — not the *proof*. The act is irreversible and the control word is
the only evidence this service has that the person asking is the person whose record it is. It is
the same gate deleting uses, because it is the same act: there is no separate objection endpoint and
no backend difference at all, only different words and a different place on the page.

> The prototype's Object button asked for nothing, because the prototype had no backend. That is the
> one place the rewrite deliberately departs from it.

## 4. Where the expiry warning went

`ExpiryBanner` now renders on **My CV**, not here. On this page the expiry is in the state sentence —
merged into it, so "paused and expiring" reads as one statement rather than two warnings competing
for the same slot, which is the case Variant C handled best and B handled worst.

Mounting the banner here as well would have been two surfaces stating one fact: §1 again. The CV
page is also where somebody actually spends time, so the warning is more likely to be seen there.

## 5. What the removal took with it

The card-stack version of this page (`BenchPauseControl`, `EraseAccountControl`,
`ContestableScores`) is gone — its controls are now rows. The properties its tests asserted were
carried over into `PrivacyDataPage.test.tsx` rather than deleted with the components: the pause
copy, the "no email on this service" clause, the delete/pause ordering, the contest control's
placement on the row it is about.

## 6. What holds it

- `PrivacyDataPage.test.tsx` (22) — every row from real data, both export labels, the paused +
  expiring merge, *Object* present only under legitimate interest, the control-word gates, the
  delete-after-pause ordering, and the owns-nothing degradation.
- `e2e/privacy-data.e2e.ts` (5) — the three things a unit suite cannot show: the export as a real
  download, pause/resume round-tripping through the API and back into the page's own prose, and
  deleting ending the session (a wrong control word changing nothing, a right one signing them out).
- `frozenHooks.test.ts` — the `row-*` hook is recorded there deliberately, which is what that guard
  is for.
