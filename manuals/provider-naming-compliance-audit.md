# Which compliance artifacts hard-name the model provider? (audit)

Audit of this repo at `main` (`e96a71a`), 2026-09-20, for P1T-243 under the map P1T-241
(*a second chat provider — Azure AI Foundry, selected by configuration*).

**The question.** A second inference provider is a new sub-processor for personal data: agent
prompts carry expert data to the model. Which parts of this repo's compliance machinery name
Google/Gemini as *the* provider, and would therefore be wrong the moment `Ai:Provider` can say
something else?

**Headline.** Twenty-two sites were audited and sixteen of them name Google or Gemini;
**four must change**, and they are all one fact told four times — the Art. 15(1)(c) recipient
category. One of those four is a string that ships to an expert's face through
`GET /api/me/access`, and two tests freeze the literal `Google`. Nothing in
the build-enforced `PersonalDataDeclaration` names a provider, so a provider swap is not a
declaration change. And **no transfer or residency claim about Gemini is written down anywhere** —
the DPIA records the Chapter V question as explicitly *unanswered* (risk R7, "Open"), so an
EU-region Foundry deployment contradicts nothing in writing; it closes an open risk rather than
changing a claim.

Sites were found by grepping `gemini|google` across `*.md`, `*.cs`, `*.ts`, `*.tsx`, `*.json`,
excluding `node_modules/`, `.claude/worktrees/`, `bin/`, `obj/`, and then by a second sweep for
`sub-processor | third country | residency | transfer | SCC | adequacy | Chapter V | EU region`.

---

## 1. The sites

### Must change — the provider name is load-bearing and ships to a data subject

| # | File : line | What it says | Why it must change |
| --- | --- | --- | --- |
| 1 | `api/Application/Compliance/Art15Disclosure.cs:62-65` | `new("Google (Gemini), as our AI model provider", "Your career narrative is sent to Google's Gemini models …")` | **The load-bearing one.** This is the Art. 15(1)(c) recipient category served verbatim by `GET /api/me/access` and rendered on the expert's privacy page. If `Ai:Provider` says Foundry and this still says Google, the service tells a data subject the wrong recipient — an Art. 5(1)(a) accuracy failure in the one disclosure the DPIA leans on as the mitigation for risk R7. |
| 2 | `api/Application/Compliance/AccessAndExportService.cs:260-264` | Derived-data note: "numeric representations (embeddings) produced by Google's Gemini models" | Same surface, second string. Embeddings are explicitly **out of scope** for the P1T-241 seam, so today this stays literally true — but it is the string that will read as a contradiction next to a changed #1, and it is the one that silently goes wrong if a future effort moves embeddings too. Must change *in wording* (attribute it to the embedding provider, not "our model provider") even though the value does not. |
| 3 | `tests/Web.Tests/TransparencyTests.cs:51-59` | `The_access_view_names_the_model_provider` asserts `r.Recipient.Contains("Google")` and `.Why.Contains("outside this company")` | A build-enforced freeze on the literal. Green here after a provider switch means the test is asserting the wrong recipient; red here is the first thing a seam PR hits. The assertion should move to "names the configured provider", keeping the `outside this company` floor. |
| 4 | `web/e2e/privacy-data.e2e.ts:38` | `await expect(page.getByText(/Google \(Gemini\)/)).toBeVisible()` | Same freeze at the real surface, against the real API. It is the only place that proves the disclosure actually reaches a rendered page, so it has to keep proving that — of whatever provider is configured. |

### Should change — documentation that would read as stale or misleading, but breaks nothing

| # | File : line | What it says | Why |
| --- | --- | --- | --- |
| 5 | `manuals/dpia-expert-workspace.md:67-71` | Flows §2 and §3: "sent to **Google's Gemini** embedding model", "go to a **Gemini** model" | The DPIA is the Art. 35 record. Naming one provider in the flow description is fine while one provider exists; with a configurable seam the flow description has to say "the configured model provider" and carry the provider list. Should change at the same time as #1, because the DPIA is the document an auditor reads against the disclosure. |
| 6 | `manuals/dpia-expert-workspace.md:83` | Recipients: "**Google (Gemini) as model provider**" | The DPIA's own recipient list, and the source of truth #1 restates. Diverging from #1 is the failure mode. |
| 7 | `manuals/dpia-expert-workspace.md:91-95` | "### Transfers outside the EEA … **Not assessed here, and it needs to be.** … which Google entity, under what mechanism" | See §3 below. The *claim* here is "we have not assessed this", which stays true — but the Google-specific phrasing becomes one of two unanswered questions rather than the only one. Should change to carry a per-provider row. |
| 8 | `manuals/dpia-expert-workspace.md:197` | Risk R7: "Named to the person by name (Gemini) … **Open.** The Chapter V transfer mechanism is not assessed" | Same. R7's mitigation column cites the naming in #1; if #1 becomes provider-dependent, R7 has to say so. This is also the row an EU deployment would *improve*. |
| 9 | `manuals/transparency-and-export.md:49-51` | "this service states three: Service Managers, clients …, and **Google (Gemini) as the model provider**" | Governing doc for the access/export slice; restates #1. Stale-doc risk only. |
| 10 | `manuals/expert-workspace-compliance.md:121-123` | "**Google (Gemini) is named as the model provider.** Before this effort the service disclosed that to nobody." | Same restatement, in the compliance overview. |
| 11 | `manuals/expert-privacy-page.md:48` | Table row: "Who sees it \| recipient categories, **including Google (Gemini) by name**" | Describes what the page renders. Follows #1. |
| 12 | `manuals/legitimate-interest-assessment.md:125` | "That their career narrative was sent to **Google's Gemini** models to be embedded." | An LIA expectation argument. Embeddings-only, so literally true after the chat seam lands; but the LIA's point is "a contractor would not expect a named third party to hold this", and a *second* named third party strengthens that argument against us. Worth a re-read when the seam lands, not a blocker. |
| 13 | `manuals/gdpr-obligations.md:221-223` | "our embeddings come from a third-party model (`gemini-embedding-001`): the CV text is *disclosed to a recipient* at embedding time" | Embeddings, out of seam scope. Model-id-specific rather than vendor-specific; stale only if embeddings ever move. |
| 14 | `manuals/gdpr-obligations.md:317` | "recipients means naming Gemini as the embedding/scoring recipient" | The word "scoring" is the chat path, so this line *does* go stale with the seam. Documentation only — this file is the research ledger, not a live disclosure. |

### No change — named, but not as a compliance claim

| # | File : line | What it says | Why it is fine |
| --- | --- | --- | --- |
| 15 | `api/Application/Compliance/PersonalDataDeclaration.cs` (whole file) | No provider named anywhere. `AgentUsage` is declared at `:122-126` as `PersonalDataAction.Delete`, `[]` fields, "Cascades with the account." | **A provider change does not touch the declaration.** The declaration is keyed on *stores that carry an `ExpertId`/`UserId`*, not on who processes them. P1T-241's note 11 (usage rows record provider identity) adds a **column to an already-declared store**, not a new store, so `PersonalDataDeclarationTests` stays green without an edit. Answers issue question 3: purely a documentation concern, with the single exception that a *new* table (e.g. a per-provider audit log keyed by user) would have to be declared like anything else. |
| 16 | `tests/Web.Tests/PersonalDataDeclarationTests.cs` | Walks the real EF model for `*ExpertId`/`*UserId` properties; no provider literal | Same reason. It asks the database what tables exist; provider identity is invisible to it. |
| 17 | `api/Application/Compliance/TransparencyNotice.cs:92-95` ("### Who sees it") | "Our Service Managers, in full. Clients see … Nobody else" | Names **no** provider, so a second provider cannot make it wrong. It is already wrong in the other direction — `manuals/transparency-and-export.md:53-58` records this as a known open gap (Art. 13(1)(e) wants recipients *at collection*, and closing it means a `CurrentVersion` bump from `2026-09-01`, re-acknowledged by every account). The seam neither creates nor closes that gap, but it makes the eventual notice text provider-shaped, which is an argument for closing it once rather than twice. |
| 18 | `web/src/pages/PrivacyDataPage.tsx:266-272` | `access.recipients.map(…)` renders `recipient.recipient` / `recipient.why` | Fully server-driven. The SPA holds no provider literal; #1 is the only source. This is the design working. |
| 19 | `web/src/pages/PrivacyDataPage.test.tsx:37,133` | Fixture string `"Google (Gemini), as our AI model provider"` and `getByText(/Google \(Gemini\)/)` | A fixture the test itself supplies, asserting the page renders *what the API sent*. It proves rendering, not the disclosure's content — #4 does that. Harmless if left; cosmetic if updated. |
| 20 | `api/Domain/Entities/AgentUsage.cs:17-18` | `/// <summary>Model that produced the response, e.g. "gemini-flash-lite-latest".</summary>` | A free-text `Model` column with an illustrative comment. Provider-agnostic by construction; the comment's example ages, nothing else. |
| 21 | `web/src/components/agent/ProposalInbox.tsx:36` · `web/src/api/agents/proposals.ts:65` | `pkg.slices.map(s => s.modelId).find(m => m) ?? "model n/a"` | Reads whatever model id the handoff package recorded — already provider-neutral. **And it is a staff surface** (inside the agents widget, via `StaffingTab`), not an expert one. |
| 22 | Expert-facing API responses generally | grep for `modelId` across `api/Application` and `api/Web` returns **nothing** | No expert-facing response names a *model*. The only provider fact an expert receives is the recipient category in #1 and the embeddings sentence in #2. Answers issue question 2: the transparency surface is exactly two strings, both server-side, both in `api/Application/Compliance`. |

Out of audit scope, listed so nobody re-greps them: `README.md:71,136-146,190,320` and `CLAUDE.md`
name Gemini as setup/config detail; `api/Agents/Configuration/Gemini*`, `api/Agents/Usage/UsageMeter.cs:32-33`,
`api/Agents/RosterScan/ScoringTransport.cs`, the `tests/Agents.Tests` live/eval suites and
`manuals/agent-cost-budgets.md` / `agent-eval-baselines.md` / `semantic-roster-search.md` are
implementation and cost documents, not compliance claims. They belong to the build tickets.

---

## 2. Verdict — build tickets, or separate effort?

**Site #1 belongs inside the build tickets; everything else is a separate, smaller effort that
should be scheduled but must not block the seam.**

The reason is that #1 is not documentation — it is a string in the Application layer that a running
service sends to a data subject, and the two tests that freeze it (#3, #4) go red the moment a
seam PR makes the provider configurable in a test environment. A build ticket that swaps
`Gemini:*` for `Ai:*` and leaves `Art15Disclosure.Recipients` hard-coded ships a service that
misdescribes its own recipients — the exact failure the Art. 15 slice was built to fix
(`transparency-and-export.md` §4: "until this slice the service named its model provider to nobody
while sending every CV to it"). So the seam's ticket set needs one slice that makes the recipient
category derive from the configured provider, with #3 and #4 rewritten to assert the *configured*
provider rather than the literal `Google`. That is Ralph-shaped work: one Application-layer change,
two test edits, literal config key, literal test names.

Everything in the "should change" block is manual prose. It is a one-sitting docs pass over five
files, best done **after** the seam lands and with the real Foundry deployment's identity in hand
(which Azure entity, which region), because writing a DPIA flow description against a resource that
does not exist yet is how the Chapter V question stayed open in the first place. Track it as one
ticket, blocked by the seam, not as seven.

The build-enforced machinery needs nothing: `PersonalDataDeclaration.cs` and its test are
provider-blind, and recording provider identity on `AgentUsage` touches a store that is already
declared.

## 3. Does an EU-region deployment change a written claim?

**No. There is no written residency or transfer claim about Gemini to change.**

The repo-wide sweep for `data residency | stored in the EU | EU region | processed in the E* |
leaves the (company|EU|EEA) | sub-processor | SCC | adequacy | Chapter V | third country` returns
**three hits, all in the DPIA, all of them statements that the question is open**:

- `dpia-expert-workspace.md:68,71` — "This leaves the company." That is a data-flow fact, not a
  residency claim, and it stays true of any external provider, EU-hosted or not.
- `dpia-expert-workspace.md:91-95` — "**Not assessed here, and it needs to be.** … The Chapter V
  transfer question — which Google entity, under what mechanism, with what supplementary measures —
  is a contracting question this team has not answered."
- `dpia-expert-workspace.md:197` — risk R7, status "**Open.** The Chapter V transfer mechanism is
  not assessed".

So the answer to issue question 4 is the useful kind of negative: the transfer position is
**assumed, not asserted**. Nothing anywhere promises an expert, a client or an auditor that their
data stays in the EEA, and nothing claims an SCC or adequacy basis for Gemini. That means:

1. An EU-region Foundry deployment **cannot contradict** anything written. It is upside only.
2. It does not automatically close R7 either. Region is not the same as transfer mechanism — an
   EU-region Azure OpenAI deployment still sits under a contract with a non-EU-headquartered
   processor, and R7 asks "which entity, under what mechanism, with what supplementary measures".
   Region narrows the question sharply; it does not answer it. The honest DPIA edit is a per-provider
   row in §1, with the EU deployment's region recorded as a *supplementary measure*, and R7
   downgraded from "Certain" likelihood of an unassessed third-country transfer to "assessed for
   one provider, open for the other".
3. The one genuinely new compliance fact the seam creates is a **second sub-processor**, and the
   repo has no sub-processor register at all — the closest thing is the three-line recipient list in
   `Art15Disclosure.Recipients`. If the deployment is ever either/or per environment rather than
   per installation, the disclosure has to name the provider *that environment actually uses*, which
   is the technical reason #1 has to become configuration-derived rather than a second hard-coded
   string.

## Sources

Every claim above is a file in this repo, cited by path and line, read at the commit this branch
was cut from. Nothing here was taken from an external write-up. The two Linear issues that frame the
question are [P1T-243](https://linear.app/p1t0ns-nest/issue/P1T-243) (this audit) and
[P1T-241](https://linear.app/p1t0ns-nest/issue/P1T-241) (the map), and the seam constraints quoted
in §2 — chat only, embeddings out of scope, either/or selection, EU region, usage rows record
provider identity — are P1T-241's own "settled while charting" list.
