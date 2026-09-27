# Data protection impact assessment: the Expert bench and the roster scan

> *2026-09: the staff actor called Service Manager throughout this document is now called Administrator. Nothing else about this assessment changed.*

> **Status (2026-09-02):** first version, shipped as P1T-192. Assesses the design as built through
> P1T-183 – P1T-191, not a plan.
>
> **This is a design input requiring a human sign-off, and it is not legal advice.** An Art. 35 DPIA
> is a controller's document. This file is the engineering team's honest description of the
> processing, the risks it can see, and the measures actually built — written so that somebody
> qualified has something concrete to review rather than a blank form. **It is not complete until a
> DPO has been consulted (Art. 35(2)) and the sign-off block at the foot is filled in.**

## Why a DPIA is required at all

Art. 35(3)(a): "a systematic and extensive evaluation of personal aspects relating to natural
persons which is based on automated processing, including profiling, and on which decisions are based
that produce legal effects concerning the natural person or similarly significantly affect" them.

The roster scan evaluates every scannable Expert's career history against a job description
automatically and ranks them, and that ranking determines who is put forward. The ICO is more direct
still: a DPIA must be done "if you decide to use AI software to help you make recruitment decisions
about candidates". **The trigger is not arguable, and this design does not argue it.**

Whether the *effect* is "similarly significant" is arguable — being passed over for an internal
staffing opportunity is not obviously a legal effect, and WP251 rev.01 draws no line for a bench.
That question is left open in [§7](#7-residual-risk) rather than resolved in our own favour, because
resolving it in our own favour is what the ICO found employers doing.

## 1. Systematic description of the processing — Art. 35(7)(a)

### What is processed

| Category | Detail | Source |
| --- | --- | --- |
| Identity and contact | name, professional title, email, phone, location | the person, or a Service Manager |
| Career history | roles, employers, dates, what they did, achievements per role | the person, or a Service Manager |
| Competence | skills with level and years, spoken languages, degrees, certifications | the person, or a Service Manager |
| Availability | a schedule of capacity percentages over time | the person, or a Service Manager |
| Account | sign-in address, registered passkeys, a **hash** of the control word | the person |
| **Derived by software** | 1536-dimension embeddings of the career narrative; scores out of 100, bands, model-written rationales, career digests | the system |
| **Roster Q&A conversations** | a Service Manager's typed questions and the model's answers, which quote names, skills, availability and CV passages of the Experts the tools returned; the model id; and the ids of every Expert a turn touched | a Service Manager, and the system |
| Accountability | the `ProcessingRecord` chain (origin, basis, reason, timestamps); the acknowledged notice version | the system |

Free-text fields — the summary and the achievement bullets — are unfiltered prose. This is where
Art. 9 exposure arises, and it is treated in [§5](#5-art-9-special-category-exposure).

### Purposes

1. Maintaining a bench record of people this company can put forward for work.
2. Assessing fit against a client's job description — **including automatically, by software that
   scores and ranks the person against other people on the bench.**
3. Preparing staffing proposals and rendered CVs to put in front of a client.
4. Letting the person read, correct, take away, pause, object to and delete their own record.

### Lawful basis

Per origin, in the database, append-only, with a CHECK constraint pairing origin to basis:
**Art. 6(1)(b)** where the person registered, **Art. 6(1)(f)** where a Service Manager entered them.
Automated decision-making rests on **Art. 22(2)(a)**, which is available only for the 6(1)(b)
population — so the 6(1)(f) population is excluded from the scan entirely. See
[`expert-workspace-compliance.md`](expert-workspace-compliance.md) §1–2.

### The flows

1. **Collection.** Self-registration with a passkey and a control word, or a Service Manager entering
   a row. Either way a `ProcessingRecord` is written before the record is usable.
2. **Indexing.** The career narrative is sent to **the configured embeddings provider** and stored
   as a `vector(1536)` against the person's id. This leaves the company. `Ai:Embeddings:Provider`
   names either **Microsoft (Azure OpenAI)** — the shipped default, `text-embedding-3-small`, on
   EU-confined `DataZoneStandard` quota — or **Google (Gemini)**, one per deployment. The key is
   **independent of `Ai:Chat:Provider`**: since
   [`adr-embeddings-provider-seam.md`](adr-embeddings-provider-seam.md) the two move separately, so
   a deployment can have one recipient doing both jobs or two doing one each.
   **On the default stack, Google receives no career narrative.**
3. **Scanning.** A job description is distilled into requirements; each scannable Expert's narrative
   is retrieved against them; the retrieved passages, skills and availability go to **the configured
   model provider** — `Ai:Chat:Provider` names either **Google (Gemini)** or **Microsoft (Azure
   OpenAI)**, one per deployment, with no failover and no per-agent routing — which returns a score,
   a band and a written rationale. This leaves the company.
4. **Proposal.** A Service Manager assembles a staffing proposal; the handoff package carries the
   evidence base. A human holds write authority throughout.
5. **Disclosure to the client.** Parts of the record go into a proposal or a rendered CV. Not the
   whole record and **not the scores.**
6. **Rights.** The access view, the JSON export, pause, objection, contest and deletion, all on one
   page.
7. **Expiry.** A sweep — off unless a deployment enables it — runs the same erasure code as a
   person deleting themselves.
8. **Roster Q&A conversations.** A Service Manager's question, the retrieved roster data and the
   last ten turns of the conversation go to **the configured model provider**, as every Roster Q&A
   question already does. What is new is that the question and the answer are **kept**: in
   Postgres, readable only by the person who asked, continued from any device, deleted by them at
   will and six months after the last question regardless. The Experts each turn touched are
   recorded by id so that erasing or pausing one of them reaches the turn. See
   [`adr-roster-qa-conversation-history.md`](adr-roster-qa-conversation-history.md).

### Recipients

Service Managers of this organisation (the record in full); the **model provider or providers**,
named to the person in the words the access view uses; clients (proposal and CV contents only).

The provider entry is one or two categories depending on what **both** `Ai:Chat:Provider` and
`Ai:Embeddings:Provider` name, because since the embeddings seam the two jobs can go to different
companies in either direction. One provider doing both is one entry; two are two, named by the job
each does:

| `Ai:Chat:Provider` · `Ai:Embeddings:Provider` | Recipient categories disclosed | What it receives |
| --- | --- | --- |
| `AzureFoundry` · `AzureFoundry` **(shipped)** | **Microsoft (Azure OpenAI), as our AI model provider** | The career narrative, embedded **within the EU**; and the retrieved passages, skills and availability with job-description context, scored outside it |
| `Gemini` · `Gemini` | **Google (Gemini), as our AI model provider** | The career narrative, embedded; and the retrieved passages, skills and availability with job-description context, scored |
| `AzureFoundry` · `Gemini` | **Google (Gemini), as our embeddings provider** | The career narrative, embedded |
| | **Microsoft (Azure OpenAI), as our AI model provider** | The retrieved passages, skills and availability with job-description context, scored |
| `Gemini` · `AzureFoundry` | **Microsoft (Azure OpenAI), as our embeddings provider** | The career narrative, embedded within the EU |
| | **Google (Gemini), as our AI model provider** | The retrieved passages, skills and availability with job-description context, scored |

An unrecognised value on either key names **both** companies rather than neither: over-telling a
data subject is survivable, naming the wrong recipient is not.

The split is derived from configuration by `Art15Disclosure.RecipientsFor` rather than written twice
(EXP-21, extended to both axes by EXP-62), so the name an expert reads is the provider their
deployment actually uses. Those bolded strings are the disclosure's own wording; an edit here that
does not also move the code makes this document and the access view disagree, which is the
divergence an auditor finds — and since EXP-68 that divergence is a red test
(`ComplianceDocumentAgreementTests`) rather than only this paragraph.

**One recipient is about the past.** A deployment that ran Gemini embeddings and has since switched
has *already* sent every narrative embedded before the switch to Google, and Art. 15(1)(c) covers
recipients to whom data "have been or will be disclosed". `EmbeddingsProviderPeriods` — one row per
interval a provider was active, holding no personal data — is what makes that answerable. Anybody
whose record already existed when a Gemini period ended reads a fifth entry,
**Google (Gemini), formerly our embeddings provider**, giving the date it ended and nothing about
what Google retained, because retention is not a fact this service knows. Google's terms are in the transfers table below,
where they belong.

### Retention

2 years from last activity for the claimed population; 6 months from collection for the unclaimed.
Calendar arithmetic. See [`retention.md`](retention.md).

Roster Q&A conversations: hard-deleted 6 months after their last question, by a sweep that is
**on by default** (it deletes transcripts, never people). This is not an Expert's Retention Clock
and does not move it.

### Transfers outside the EEA

**Partly assessed, and the rest needs to be** — once per provider, and now once per *job*, because
the two paths sit on different quota tiers. The design names each recipient and discloses it to the
person, which is the transparency duty. The Chapter V transfer question is a contracting question,
and it is answered for one of the three rows below:

| Provider · job | Receives | What is known about region and entity | Still unanswered |
| --- | --- | --- | --- |
| **Microsoft (Azure OpenAI)** — embeddings, where `Ai:Embeddings:Provider` is `AzureFoundry` (**the shipped default**) | The career narrative | `text-embedding-3-small` on **`DataZoneStandard`** in Sweden Central, capacity 120 (EXP-56). **This one is EU-confined**: Data Zone quota keeps inference inside the EU, which is what the region alone never did. EU processing costs 10% more — about $0.001 per full roster embed — see [`adr-embeddings-provider-seam.md`](adr-embeddings-provider-seam.md) §7 | Which Microsoft entity, under what mechanism. Residency is settled; the contract is not |
| **Microsoft (Azure OpenAI)** — scoring, where `Ai:Chat:Provider` is `AzureFoundry` (**the shipped default**) | The retrieved passages, skills and availability, with job-description context | `gpt-4.1-mini` on `GlobalStandard` in Sweden Central. **The region is not a residency measure**: `GlobalStandard` does not confine inference to the EU, and Data Zone capacity for this model is not purchasable on this subscription — see [`adr-chat-provider-seam.md`](adr-chat-provider-seam.md) §7 | Which Microsoft entity, under what mechanism, with what supplementary measures |
| **Google (Gemini)** — embeddings and/or scoring, where either key names `Gemini`. **Not used with real data** (see the rule below) | The career narrative, the scoring context, or both, depending on which keys name it | Researched from Google's published terms, 2026-09-27 (EXP-63): on a **billed** project prompts and responses are logged **55 days** for abuse monitoring only, are not used for training, and authorised Google staff may read flagged content; processing is covered by Google's **Data Processing Addendum**; the contracting entity for an EEA customer outside FR/IT/PL is **Google Cloud EMEA** Limited (Ireland); transfers rely on the **EU–US Data Privacy Framework** with Google's SCCs as fallback. There is **no data-residency commitment** on the Gemini Developer API and no zero-retention option — Google's own docs point to Vertex AI for that. On the **unpaid** tier none of the above holds: content is used to improve Google's products, human reviewers may read it, no retention period is published, and the terms say not to submit personal information | Whether the DPA in fact binds an EEA operator's free-tier traffic (Google's service list names only Paid Services); whether a deletion request under DPA §6.1.2 would reach an `embedContent` call at all |

So the position is no longer one sentence. **Embeddings on the shipped stack are EU-confined**;
**chat is not**, and choosing an EU region bought it no residency. Both Microsoft rows still lack an
assessed entity and mechanism. The Google row is researched but is not a live transfer on any
deployment this team runs.

**Gemini is for development and demo data only.** This is a rule the code cannot enforce, so the
DPIA carries it. Google's terms permit **only Paid Services** when making an API client available to
users in the EEA, UK or Switzerland, and the unpaid terms say in as many words not to submit personal
information. A deployment holding real people therefore runs Azure, or takes a **separate, recorded
decision** to use a billed Gemini project — which is a new entry here, not a config change. Every
deployment so far has held synthetic demo data and development data only, so **no real person's
career narrative has been sent to Google**, and there is no retroactive notice owed to anybody.

All three rows are carried as residual risk R7.

## 2. Necessity and proportionality — Art. 35(7)(b)

### Is the processing necessary for the purposes?

**The record itself:** yes, and largely uncontroversial. A bench that does not hold career histories
cannot put anybody forward.

**The automated assessment:** this is the load-bearing claim, and it is stated in full in
[`art22-safeguards.md`](art22-safeguards.md) §2. In short: the Expert registered in order to be
placed; assessment is not incidental to that relationship, it *is* the relationship; and reading
every CV against every job description by hand is not a slower version of this service but a
different one that does not exist. The alternative to automated assessment at bench scale is no
assessment, which is no placement.

Two limits are recorded with the argument so nobody has to rediscover them: **it only covers people
who asked** (which is why the LI population is excluded), and **it is an argument, not a licence** —
a future feature scoring an Expert for a purpose other than a Job needs its own basis.

### Proportionality — what was declined

Proportionality is easier to demonstrate by what was refused than by what was built.

| Declined | Why |
| --- | --- |
| A 5-year discrimination-defence archive (CNIL suggests one) | A second restricted store with its own disclosure, retention and erasure path, for litigation that will not come |
| An access log of who viewed whom | Answering a disclosure duty by manufacturing a large new store of personal data about access |
| Classifying free text for special-category content on save | It creates the very Art. 9 inference it aims to avoid |
| Any inference of protected characteristics, ever | Art. 9 processing "whether your inference about a candidate is correct or not" (ICO). A **standing** prohibition on all future retrieval, ranking and scoring work |
| Scoring the LI population | 22(2)(a) does not reach them |
| A grace window on deletion | There is no email with which to undo it, so a window would only delay the act while keeping the data |
| Email of any kind | A product decision (P1T-167). **It is the direct cause of R1 and R2 below** |

### Data minimisation as built

The export excludes derived data, because portability does not cover it. The scores are excluded
from what a client sees. The control word is stored only as a hash. `DataExportRecord` holds no field
of the Expert's beyond an id. A `StaffingProposalCandidate` that must outlive the person keeps only
an id and scores, and deliberately holds **no foreign key**, so the id is a restricted-processing
reference rather than a link.

## 3. Rights, and how each is actually delivered

| Right | Where | Note |
| --- | --- | --- |
| Art. 13/14 information | versioned transparency notice, acknowledged and recorded | **Art. 14 is not delivered for staff-created rows — see R1** |
| Art. 15 access | `GET /api/me/access`, rendered on `/me/privacy` | Includes derived data: scores, bands, rationales, digests. For Roster Q&A conversations, **existence only** — how many answers referenced the person, between which dates, by which model — never the text, which is largely about third parties (Art. 15(4)). **A legal judgement for the DPO**, not settled by this document |
| Art. 15(1)(h) logic | the same page, in prose | Concedes the automation rather than claiming human review |
| Art. 16 rectification | the CV editor | Email is immutable to the person who benefits from changing it (claim integrity) |
| Art. 17 erasure | `Delete everything`, control-word re-auth | Irreversible, no grace window, ends sessions on both hosts. Roster Q&A turns that touched the person, or name them in full, are emptied and marked Removed in the same act |
| Art. 18 restriction | the scrub residue is treated as restricted, not anonymous | Explicitly **not** presented as anonymisation |
| Art. 20 portability | JSON download | Labelled a right under 6(1)(b), a courtesy under 6(1)(f); same payload |
| Art. 21 objection | LI rows only | **Not adjudicated** — honoured as deletion |
| Art. 22(3) safeguards | contest → queue → a Service Manager reviews | Plus `ContestNote`, the person's own words, shown first |
| Complaint to an SA | stated on the page | |
| — | pause (`HiddenAt`) | Not a GDPR right; built because "stop offering me" should not require deletion. Also hides Roster Q&A turns about the person, and keeps them out of any replay to the model, until they unpause |

## 4. Security and access control

- **Passkeys only.** No password store exists to breach.
- **Default-deny endpoint classification**, asserted by a test that walks the live endpoint table, so
  a new endpoint cannot be unclassified.
- **Ownership scope** at the Application layer: an Expert reads their own row and nothing else. A
  foreign row is **404, never 403** — an existence claim about somebody else's record is itself a
  disclosure. Coverage is enforced by reflection over every service, with written exemptions.
- **Role split** between Expert and Service Manager, with a `TokenVersion` so a role change or an
  erasure invalidates live sessions without a revocation list.
- **Control-word re-authentication** for the irreversible acts.

## 5. Art. 9 special-category exposure

An Expert may write health, religion, union membership or ethnic detail into a free-text field
without intending to, and semantic retrieval then indexes it. The measures built:

- Guidance on every free-text field, naming the categories rather than saying "sensitive
  information" — one string, so the ask cannot drift into three wordings.
- The same paragraph in the transparency notice, so it reaches people who never open the editor.
- A standing prohibition: **never build search or filtering on such signals, never infer protected
  characteristics.**

**Stated plainly: this is a mitigation and not a solution.** Nothing here prevents such content being
written and nothing removes it once written. Whether unfiltered CV prose plus semantic retrieval is
Art. 9 processing at all is genuinely unsettled (C-184/20 §§122–128), and the two alternatives were
rejected for reasons that do not go away: explicit consent under 9(2)(a) cannot be informed about
content the person does not know they are about to write, and classifying text on save creates the
inference it is trying to avoid. Residual risk R3.

## 6. Risks to rights and freedoms, and the measures against them — Art. 35(7)(c)–(d)

Likelihood and severity are this team's engineering judgement about the system as built. They are
inputs to a DPO's assessment, not a substitute for it.

| # | Risk to the person | Sev. | Lik. | Measures actually built | Residual |
| --- | --- | --- | --- | --- | --- |
| R1 | **A person is held without ever being told** (Art. 14). A Service Manager enters a real person who cannot be reached | High | **High** | 6-month clock from collection; excluded from the scan; invisible to the Art. 22 route; the row is visibly degraded until claimed | **High — unresolved.** The clock drains the population; it does not discharge the duty |
| R2 | An Expert never sees an updated notice (Art. 13 on change) | Medium | Medium | Notice is versioned and the acknowledged version recorded, so the gap is *visible* rather than silent | Medium — unresolved for the same reason as R1 |
| R3 | Special-category detail in free text is stored and indexed — now including a **Service Manager's typed Roster Q&A question** about a person, kept for up to six months | High | Medium | Field guidance naming the categories; the same text in the notice; standing prohibition on inference and on filtering by such signals; for conversations, owner-only reading, owner delete and a six-month sweep that is on by default | Medium. A mitigation, not a solution; no content filter is applied to questions |
| R4 | **Being ranked out by software with no human ever reading the record** | High | High *(inherent to the design)* | Automation conceded rather than denied; 22(2)(a) relied on with the necessity argument written down; all three 22(3) safeguards built on the decision row; **the score, band and rationale are shown to the person in full**, which is what makes contesting possible; LI population excluded entirely | Medium. Depends on contests actually being reviewed by somebody with authority to change the outcome — an operational fact, not a code property |
| R5 | A model-written rationale — or a kept Roster Q&A answer — is wrong, unfair, or humiliating | Medium | Medium | The subject reads a rationale verbatim, which constrains what may be written; contest reopens it; no inference of protected characteristics; the model gets passages, skills and availability only. A conversation answer is **not** shown to its subject (existence only, R13), so that constraint does not reach it; every answer is forced to rest on a fresh tool call and ungrounded answers are marked | Medium |
| R6 | Personal data survives a deletion request | High | **Low** | One erasure path; a declaration with a mandatory reason per store; a test walking the real EF model transitively through FKs; cascades in the database rather than in code; the embedding is destroyed with the text it derives from; Roster Q&A turns scrubbed by touched id **and** by full name, which the name-sweep test covers | Low. A typed nickname or misspelling in a conversation survives — the same gap proposal free text already has |
| R7 | The career narrative leaves the company to a third-party model provider — one company on the shipped stack, two where the keys name different ones | Medium | Certain | Each recipient named to the person by name and split by the job it does, derived from **both** provider keys so a name cannot go stale in either direction; a **former** recipient named in the past tense to anybody whose record predates a switch, from `EmbeddingsProviderPeriods`; passages rather than the whole record at assessment time; no inference permitted | **Reduced, not closed.** *Google:* **closed on the default stack** — since EXP-67 embeddings ship on Azure, so Google receives nothing and no real person's data ever reached it. It reopens only on a deployment that names `Gemini` on either key, which the §1 rule confines to development and demo data. *Microsoft:* now **two transfers on different tiers**. Embeddings are EU-confined on `DataZoneStandard`, which is a real residency measure and the one improvement this effort bought. Chat stays `GlobalStandard`, so scoring is **not** EU-confined and the region still buys nothing (chat ADR §7). The Microsoft entity and mechanism remain unassessed for both. Assessed is not resolved. See §1 |
| R8 | Somebody's record is read by a party who should not see it | High | Low | Passkeys only; default-deny endpoint classification with a test; ownership scope with reflective coverage; 404 not 403; `TokenVersion` |
| R9 | Erasure is triggered by somebody who is not the person | High | Low | Control-word re-auth on erasure and objection; the same gate on both, because they are the same act | Low |
| R10 | A person deletes when they meant to pause | Medium | Medium | Two separate controls, deliberately far apart on a long page; the page length **is** the mechanism, and a test asserts the ordering so "tidying" cannot undo it | Low–Medium. There is no email with which to undo a mistake |
| R11 | A record expires and is deleted while still wanted | Medium | Low | Reading your own record is activity and pushes the date back; a 30-day final-warning banner on the page people actually use; **the sweep is off unless a deployment enables it** | Low |
| R12 | The scrub residue is treated as anonymous, and reused freely | Medium | Low | Documented as pseudonymisation under Art. 18 in the declaration, the manual and the code comments; no FK, so the id cannot be joined back as a link | Low, while the documentation holds |
| R13 | **A person is discussed in a kept conversation they cannot read** | Medium | High *(inherent to the design)* | Owner-only reading; the person's access view says how often, when and by which model, without the text (Art. 15(4)); erasure empties the turns and pause hides them; six-month sweep on by default | Medium. Whether existence-only discharges Art. 15 for this store is **for the DPO** |

## 7. Residual risk

**Two risks do not reduce to acceptable with anything in this repository: R1 and R7.**

R1 is a duty this design does not discharge. It is recorded as an unresolved breach rather than a
managed risk, and it is a direct consequence of the decision never to send email. That decision
should be re-taken with this cost in view rather than inherited: **both notice gaps close immediately
if email is added.**

R7 is no longer one thing. Its **residency** half was an engineering question and has been answered
for embeddings: Data Zone quota confines them to the EU, and the shipped stack sends Google nothing.
Its **contracting** half — which Microsoft entity, under what mechanism — is not an engineering gap
and sits outside what this team assessed, for both the chat and the embeddings path. Chat's
residency half also stays open, because `GlobalStandard` is the only tier its model has here.

R4 is inherent to the product rather than a defect in it. The design's answer is to concede it, argue
necessity in writing, build all three safeguards, and show the person what was written about them.
Whether that answer is sufficient is exactly the question a DPO exists to answer.

**Whether Art. 36 prior consultation is required has not been assessed.** If a reviewer reads R1 or
R4 as high residual risk, it may be.

## 8. Review triggers

Re-open this DPIA when any of the following happens, not on a calendar:

- **Either** provider key changes — chat or embeddings — or a third provider is added. The two move
  independently now, so a switch on one is a review trigger on its own.
- **A deployment is pointed at Gemini while holding real people's data.** That is the §1 rule being
  broken, and it needs a billed project and an entry here before it happens, not after.
- Azure chat gets Data Zone quota. Then both paths are EU-confined and R7's residency half closes.
- Anything is scored for a purpose other than assessment against a Job — this is the case the
  necessity argument explicitly does **not** cover.
- The LI population is brought into the scan for any reason.
- Email is added (this improves R1 and R2 and should be recorded as such).
- Any new store of personal data is declared, or an existing one changes action.
- Retention periods change.
- Contest volumes or override rates show that reviews are not meaningfully changing outcomes — the
  operational fact R4's residual rating depends on.

## 9. Sign-off — required, and not yet given

| | |
| --- | --- |
| Prepared by | The engineering team, as part of P1T-192 |
| Date prepared | 2026-09-02 |
| DPO consulted (Art. 35(2)) | **Not yet — required** |
| Controller sign-off | **Not yet — required** |
| Art. 36 prior consultation assessed | **No** |
| Views of data subjects sought (Art. 35(9)) | **No.** Practicable here — the bench population is reachable in-app — and not done |

Until the rows above are filled in, this document is a description of a design, not an assessment
anybody has accepted.
