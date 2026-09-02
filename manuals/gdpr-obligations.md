# GDPR obligations for the ExpertToJob data set (research, P1T-168)

What the regulation actually requires of a system that holds Expert CVs, embeds them for
retrieval, and copies them into staffing decision ledgers — so the consent, erasure and export
slices are designed against the text and the guidance rather than against intuition. Every claim
below is traced to the regulation, an EDPB/WP29 document, a CJEU judgment or a national DPA's own
guidance; secondary commentary was not used.

> Decision trail: wayfinder map **P1T-167** — research **P1T-168** (this file).
> This is a **design input, not legal advice**. Points that genuinely need a DPO or counsel are
> collected in [§9](#9-what-actually-needs-a-dpo-not-us) and are not softened elsewhere.

**Headline: the lawful-basis choice is not a formality that can be revisited later — it decides
whether the export right exists at all, and consent cannot be swapped for legitimate interest
after the fact.** Art. 20 only bites where processing rests on consent or contract (Art. 20(1)(a),
Recital 68), and EDPB Guidelines 05/2020 §123 forbids retrospectively falling back to legitimate
interest when consent turns out to be invalid. Three further findings reshape the slices: derived
stores (chunks, embeddings, digests, scores) are **in** scope for access and erasure but **out** of
scope for portability; scrubbing names while keeping ids and scores is pseudonymisation, not
erasure; and "a human approves the proposal" does not by itself take us out of Art. 22 — the ICO
studied this exact pattern in 2026 and found most such employers were making solely automated
decisions in practice.

> **Landed by the docs slice (P1T-192), unchanged.** This file is the research as it was written on
> 2026-09-01, before any of it was built. It is kept verbatim on purpose: it is the record of what
> the sources said *before* the design had to commit, and editing it after the fact to agree with
> what shipped would destroy the only thing it is good for. Two consequences for the reader:
>
> - **The schema names predate the rename.** `Employee`, `EmployeeId` and `EmployeeSearchChunk`
>   appear throughout; read them as `Expert`, `ExpertId` and `ExpertSearchChunk` (P1T-167).
> - **Several of its open questions are now answered**, and the answers are not here. What was
>   built, what it decided, and where it diverged from this research is
>   [`expert-workspace-compliance.md`](expert-workspace-compliance.md). Read that one for the
>   design; read this one for why.

## 1. What the system holds, in GDPR terms

Characterisation of each store, with the code fact that makes it matter for erasure. FK/cascade
facts were read off `api/Infrastructure/Persistence/AppDbContext.cs` and the entities.

| Store | Personal data? | Cascade on `Employee` delete? | Notes |
|---|---|---|---|
| `Employee` + children (languages, availability, skills, qualifications, experiences, achievements) | Yes, directly | Cascade (`DeleteBehavior.Cascade` on each `EmployeeId` FK) | The uncontroversial core |
| `EmployeeSearchChunk.Content` | Yes — it *is* the CV text, rendered | **Cascade** (FK + cascade configured) | Erasure already reaches it |
| `EmployeeSearchChunk.Embedding` (`vector(1536)`, `gemini-embedding-001`) | Yes — see [§4](#4-derived-stores-chunks-and-embeddings) | Same row, so cascade | Not a "model"; a per-person record |
| `ScoringJobCandidate` (`Name`, `Title`, `Digest`, `Score`, `Band`, `Rationale`) | Yes | **No FK to `Employee`, no cascade** — `EmployeeId` is a bare `Guid` | Survives deletion of the Expert, carrying a full career narrative |
| `StaffingProposalCandidate` (`Name`, `Title`, `Rank`, `MatchScore`, `MatchBand`, `Rationale`) | Yes | **No FK, no cascade** | Same |
| `StaffingProposal.PackageJson` (jsonb) | Yes — contains a full CV dump plus provenance and rationale | No — it is opaque jsonb | The single largest erasure hazard |
| `AgentUsage` | Yes, about the *staff user* (FK to `User`, cascade), not the Expert | n/a | Different data subject; do not conflate |

The shape of the erasure problem is therefore already visible in the schema: **the two stores that
would survive an Expert's deletion today are precisely the two that hold a generated narrative
about them.**

## 2. Lawful basis (Art. 6)

### The text

Art. 6(1)(f) permits processing "necessary for the purposes of the legitimate interests pursued by
the controller or by a third party, except where such interests are overridden by the interests or
fundamental rights and freedoms of the data subject". Recital 47 grounds it in **reasonable
expectations**: "the existence of a legitimate interest would need careful assessment including
whether a data subject can reasonably expect at the time and in the context of the collection of
the personal data that processing for that purpose may take place", and rights "could in particular
override the interest of the data controller where personal data are processed in circumstances
where data subjects do not reasonably expect further processing."

EDPB Guidelines 1/2024 on Art. 6(1)(f) sets three cumulative conditions (§6): a legitimate interest
pursued; the processing being *necessary* for it; and the balancing test not tipping to the data
subject. §17 adds that the interest must be "lawful", "clearly and precisely articulated" and "real
and present, and not speculative". §9 is explicit that Art. 6(1)(f) "cannot be considered as a
legal basis 'by default'".

### Art. 7 validity, and whether a bench can meet it

Art. 7 requires the controller to demonstrate consent (7(1)), present it distinguishably (7(2)),
allow withdrawal at any time where "it shall be as easy to withdraw as to give consent" (7(3)), and
weighs conditionality heavily against freeness (7(4)). EDPB Guidelines 05/2020 sharpens each:

- **Freely given / detriment** (§46–48): "the controller needs to demonstrate that it is possible
  to refuse or withdraw consent without detriment"; withdrawal must be "free of charge or without
  lowering service levels" (§115).
- **Granular** (§§42–45, 60): "If the controller has conflated several purposes for processing and
  has not attempted to seek separate consent for each purpose, there is a lack of freedom."
- **Withdrawal mechanics** (§§113–114): withdrawal must be possible "via the same electronic
  interface", because "switching to another interface for the sole reason of withdrawing consent
  would require undue effort", and "free of charge or without lowering service levels".
- **After withdrawal** (§117, §119): processing that has already happened stays lawful, but the
  controller "must stop the processing actions concerned", and has "an obligation to delete data
  that was processed on the basis of consent once that consent is withdrawn, assuming that there is
  no other purpose justifying the continued retention" (Art. 17(1)(b)).

The load-bearing one for us is **§123**: *"the controller cannot swap from consent to other lawful
bases. For example, it is not allowed to retrospectively utilise the legitimate interest basis in
order to justify processing, where problems have been encountered with the validity of consent."*
§120 adds that even a forward-looking change of basis must be notified under Arts. 13/14.

Can a staffing bench meet Art. 7? On the sources, **yes in principle but only if withdrawal is
real**. There is no employer/employee imbalance in the EDPB's sense between a staffing platform and
a self-registering contractor — Guidelines 05/2020 §21 confines that analysis to employment, though
§24 warns that "imbalances of power are not limited to public authorities and employers", so it is
a judgement call rather than a free pass. The practical bar is §46: if withdrawing consent removes
the Expert from the bench and therefore from paid work, an authority could read that as detriment —
which is exactly the argument that pushes recruitment toward legitimate interest.

### The two origin cases

| Origin | What the sources say |
|---|---|
| **Expert self-registers and uploads their own CV** | Both bases are available. Consent is coherent (the act of registering is an affirmative act for a specific purpose) and it is the only route that *creates* an Art. 20 export right. Art. 6(1)(b) (contract) is the third candidate and also preserves Art. 20 — but only where the processing is genuinely *necessary* for a contract with the Expert, which is a stretch while the bench is a marketing asset rather than a service the Expert contracts for. |
| **A Service Manager creates the row first** | Consent is not available at collection — there is no data subject present to give it. The basis at that moment can only be Art. 6(1)(f), and Arts. 14(1)–(2) transparency-to-a-third-party duties attach (including Art. 15(1)(g) source disclosure later). Retro-fitting consent when the Expert first logs in is a **change of basis**, which §120 permits only with notification, and which does not retroactively validate the earlier collection. |

**The thing this changes in the design:** the two origins cannot share one basis silently. Either
the model records a per-Expert lawful basis and origin (and the export/erasure code branches on
it), or the product commits to legitimate interest for the roster and treats consent as a separate,
narrower grant for specific extras — accepting that legitimate interest means **no Art. 20 right,
but a live Art. 21 objection right** the design must implement instead.

And the choice cannot be made once, globally: the ICO and the CNIL give **opposite answers for a
CV pool**, which is exactly our bench. See [§8](#8-national-dpa-guidance-ico-and-cnil-do-not-agree).

## 3. Erasure (Art. 17)

### What the exceptions actually let a controller keep

Art. 17(1) grounds include (a) data no longer necessary, (b) consent withdrawn with no other legal
ground, and (c) a successful Art. 21 objection. Art. 17(3) then disapplies erasure **"to the extent
that processing is necessary"** for: freedom of expression; a legal obligation or public-interest
task; public health; Art. 89(1) archiving/research/statistics; and — the only one plausibly ours —
**(e) "the establishment, exercise or defence of legal claims"**.

Does the decision record of a staffing proposal fall under 17(3)(e)? **Plausibly, but narrowly, and
the sources do not settle it.** Three observations from the text:

1. The carve-out is scoped by *necessity*, not by category. It licenses keeping what is needed to
   defend a claim — that a decision was made, by whom, when, on what basis — not the whole
   `PackageJson` CV dump. Keeping the full dump under 17(3)(e) is not supportable from the text.
2. There is no "business records" or "audit trail" exception in Art. 17(3). If the retention motive
   is internal accountability rather than a legal claim, 17(3) does not cover it and the honest
   route is a documented retention period plus Art. 5(1)(e) storage limitation.
3. **Art. 18 is the mechanism the regulation actually offers here.** Art. 18(1)(c) lets the
   *controller* stop needing the data while the *data subject* requires it for legal claims, and
   Art. 18(2) restricts a restricted record to storage only. A design that moves a decided proposal
   into a restricted, non-queryable state is closer to the regulation's own model than one that
   keeps it live and calls it an exception.

### Scrubbing personal fields but keeping ids and scores — erasure or pseudonymisation?

**Pseudonymisation, on the current sources, unless anonymity is separately proven.** This is the
sharpest finding in the file.

Art. 4(5) defines pseudonymisation as processing such that data "can no longer be attributed to a
specific data subject without the use of additional information". Recital 26 says pseudonymised
data "should be considered to be information on an identifiable natural person", and sets the test
as "all the means reasonably likely to be used, such as **singling out**, either by the controller
or by another person".

EDPB Guidelines 01/2025 on Pseudonymisation §22 is directly on point and worth quoting in full:

> Pseudonymised data, which could be attributed to a natural person by the use of additional
> information, is to be considered information on an identifiable natural person, and is therefore
> personal. This statement also holds true if pseudonymised data and additional information are not
> in the hands of the same person. […] **Even if all additional information retained by the
> pseudonymising controller has been erased, the pseudonymised data becomes anonymous only if the
> conditions for anonymity are met.**

Applied to us: nulling `Name`/`Digest`/`Rationale` on `ScoringJobCandidate` while keeping
`EmployeeId`, `Score` and `Band` leaves a record that still singles out one person, and deleting the
`Employee` row does not automatically make the residue anonymous — the controller has to
*demonstrate* that attribution is no longer possible by means reasonably likely, which our backups,
`PackageJson` blobs and any external CV copy work directly against. So the residue is still personal
data and still erasable on request.

The practical consequence: **"scrub and keep" is a legitimate design only if it is justified under
Art. 17(3)(e) or Art. 18 as retained personal data with a stated purpose and retention period — not
if it is presented as having satisfied the erasure request.**

## 4. Derived stores: chunks and embeddings

Two separate questions, and only one of them needs EDPB Opinion 28/2024.

**`EmployeeSearchChunk.Content` is trivially personal data.** It is the Expert's own summary,
experience or achievement text, rendered verbatim and stored against their `EmployeeId`. No test is
needed; Art. 4(1) covers it.

**The 1536-dim embedding is also personal data, and Opinion 28/2024 is the wrong tool for the easy
part of that.** Opinion 28/2024 is about *AI models* — "the product resulting from the training
mechanisms […] applied to a set of training data" (§21), with the Opinion's scope limited to models
"that are the result of a training of such models with personal data" (§26). Our embedding is not
that. It is a per-person derived record in a table row with an `EmployeeId` FK, a `ContentHash` of
the source text, and a `Model` column. Recital 26's "singling out" test resolves it directly: the
row picks out exactly one person and is joined to their identity by a foreign key.

Opinion 28/2024 is still worth citing for the argument it forecloses — "it's just a vector of
floats, so it isn't personal data":

- §29: some AI models "are specifically designed to provide personal data regarding individuals
  whose personal data were used to train the model, or in some way to make such data available. In
  these cases, such AI models will inherently (and typically necessarily) include information
  relating to an identified or identifiable natural person, and so will involve the processing of
  personal data. **Therefore, these types of AI models cannot be considered anonymous.**" Our
  retrieval index exists precisely to make a person's CV findable — it is on the wrong side of that
  line by design.
- §31: information "may still remain 'absorbed' in the parameters of the model, namely represented
  through mathematical objects. They may differ from the original training data points, but may
  still retain the original information of those data". A mathematical representation is not
  laundered by being mathematical.
- §43: the standard for anonymity is that both direct extraction and inadvertent obtaining via
  queries "should be **insignificant** for any data subject", assessed on "all the means reasonably
  likely to be used".
- §§56–58: anonymity is an **accountability claim the controller must document**, not a default.
  If a supervisory authority cannot confirm it from the documentation, "the SA would be in a
  position to consider that the controller has failed to meet its accountability obligations under
  Article 5(2) GDPR."

**Design consequence:** embeddings must be erased with the Expert, not left behind as "anonymised
vectors". Today they already are — the chunk table cascades on `EmployeeId`. That behaviour is now
a compliance property and should get a test, not just a schema default. Note also that our
embeddings come from a third-party model (`gemini-embedding-001`): the CV text is *disclosed to a
recipient* at embedding time, which is an Art. 15(1)(c) recipient disclosure and an Art. 13/14
transparency item.

## 5. Portability (Art. 20)

### Whether it applies at all

Art. 20(1)(a): the right exists only where "the processing is based on consent […] or on a
contract". Recital 68: "It should not apply where processing is based on a legal ground other than
consent or contract." WP29 WP242 rev.01 (endorsed by the EDPB) restates this and adds that under
legitimate interest, portability is **good practice, not an obligation** (p. 8 and n.16).

So: **if the roster runs on legitimate interest, there is no Art. 20 duty at all.** Building an
export is then a product decision, not a compliance one — but Art. 15(3) still requires a *copy* of
the personal data, so an export surface is needed either way. The difference is what goes in it.

### Scope: provided, observed, inferred

WP242 rev.01 (pp. 9–11) draws the line the design has to encode:

- **In**: "Data actively and knowingly provided by the data subject" and "Observed data provided by
  the data subject by virtue of the use of the service or the device."
- **Out**: "inferred data and derived data are created by the data controller on the basis of the
  data 'provided by the data subject'"; the term "must be interpreted broadly, and should exclude
  'inferred data' and 'derived data', which include personal data that are created by a service
  provider (**for example, algorithmic results**)". And: data "created by the data controller as
  part of the data processing, e.g. by a personalisation or recommendation process, by user
  categorisation or profiling […] are not covered by the right to data portability."

Mapped onto our stores:

| Field | Art. 20 | Art. 15 |
|---|---|---|
| CV fields the Expert typed (summary, experiences, achievements, skills, languages, availability, contact) | In | In |
| `EmployeeSearchChunk.Content` (a *rendering* of their own text) | Arguably in — it is their text, reformatted. WP242 does not address re-rendered source text; treating it as in-scope is the safer and cheaper call | In |
| `EmployeeSearchChunk.Embedding` | Out — created by the controller | In |
| `ScoringJobCandidate.Digest`, `Score`, `Band`, `Rationale` | Out | In |
| `StaffingProposalCandidate.Rank`, `MatchScore`, `MatchBand`, `Rationale` | Out | In |
| `StaffingProposal.PackageJson` | Mixed: the CV dump inside it is in, the provenance/rationale is out | In, in full |

WP242 anticipates exactly this: "data portability implies an additional layer of data processing by
data controllers, in order to extract data from the platform and **filter out personal data outside
the scope of portability, such as inferred data**".

### Format

Art. 20(1): "structured, commonly used and machine-readable". Recital 68 adds "interoperable".
WP242 (p. 16) treats the three as "a set of minimal requirements", quotes Recital 21 of Directive
2013/37/EU for "machine readable" — "a file format structured so that software applications can
easily identify, recognize and extract specific data […] Documents encoded in a file format that
limits automatic processing, because the data cannot, or cannot easily, be extracted from them,
should not be considered to be in a machine-readable format" — and declines to mandate a specific
format, ruling out only formats "subject to costly licensing constraints".

**Two concrete consequences.** First, **our rendered CV PDF does not satisfy Art. 20** — see
[`cv-pdf-render.md`](cv-pdf-render.md); a PDF is the paradigm case of a format that limits automatic
processing. JSON is fine. Second, Art. 20(2)'s direct controller-to-controller transmission is
bounded by "where technically feasible", and Recital 68 states it "should not create an obligation
for the controllers to adopt or maintain processing systems which are technically compatible" — so
no import/export partner integration is owed.

Finally, WP242 (p. 7): "Data portability does not automatically trigger the erasure of the data […]
and does not affect the original retention period", and conversely portability "cannot be used by a
data controller as a way of delaying or refusing" erasure. Export and delete are independent
flows.

## 6. Transparency and access (Art. 15), and Art. 22

### What a "what we hold on you" view must show

Art. 15(1) fixes the field list, and it is a *field list*, not a vibe: (a) purposes; (b) categories
of personal data; (c) recipients or categories of recipient, "in particular recipients in third
countries"; (d) "where possible, the envisaged period for which the personal data will be stored,
or, if not possible, the criteria used to determine that period"; (e) the rectification/erasure/
restriction/objection rights; (f) the right to complain to a supervisory authority; (g) "where the
personal data are not collected from the data subject, **any available information as to their
source**"; (h) the existence of Art. 22 decision-making plus "meaningful information about the logic
involved, as well as the significance and the envisaged consequences". Art. 15(3) then requires "a
copy of the personal data undergoing processing".

EDPB Guidelines 01/2022 on the right of access adds three things we need:

- **Scope includes everything derived** (§99): "like most data subject rights, the right of access
  includes both inferred and derived data, including personal data created by a service provider,
  whereas the right to data portability only includes data provided by the data subject." Its
  §97 list names "algorithmic results", "credit ratio", "classification based on common attributes"
  and pseudonymised data explicitly.
- **Example 16** is our case almost exactly: elements used to reach a decision about an employee —
  "ranking, career potential" — are that person's personal data and are accessible on request,
  subject to Art. 15(4) where they reveal a third party.
- **Self-service is an accepted delivery mechanism** (§137), with the caveat in §138: "The use of
  self-service tools should never limit the scope of personal data received", and requests arriving
  outside the tool must still be handled.

For us, (c), (d) and (g) are the ones the current system cannot answer: recipients means naming
Gemini as the embedding/scoring recipient; retention means committing to periods we have not set;
source means recording whether a row came from the Expert, a Service Manager, or an ingested
document.

### Does human approval escape "solely automated"?

**Not automatically, and on the current pipeline shape it is genuinely at risk.** Recital 71 names
"e-recruiting practices without any human intervention" as a paradigm Art. 22 case, so the domain is
squarely in scope.

CJEU **C-634/21 SCHUFA Holding (Scoring)** (7 December 2023) sets three cumulative conditions (§43):
a "decision"; "based solely on automated processing, including profiling"; producing legal or
similarly significant effects. The ruling's operative part then holds that the automated
establishment of a probability value *is itself* Art. 22 decision-making "where a third party, to
which that probability value is transmitted, **draws strongly on that probability value** to
establish, implement or terminate a contractual relationship with that person." §50: where the value
"plays a determining role", "the establishment of that value must be qualified **in itself** as a
decision". §61 gives the reason: a restrictive reading treating scoring as a "preparatory act" would
create "a risk of circumventing Article 22 […] and, consequently, a lacuna in legal protection".

WP29 WP251 rev.01 (endorsed by the EDPB) supplies the test for the human step (p. 21):

> The controller cannot avoid the Article 22 provisions by fabricating human involvement. For
> example, if someone routinely applies automatically generated profiles to individuals without any
> actual influence on the result, this would still be a decision based solely on automated
> processing. To qualify as human involvement, the controller must ensure that any oversight of the
> decision is **meaningful, rather than just a token gesture**. It should be carried out by someone
> who has the **authority and competence to change the decision**. As part of the analysis, they
> should consider all the relevant data.

It also instructs controllers to "identify and record the degree of any human involvement in the
decision-making process and at what stage this takes place" as part of the DPIA.

Where that leaves us. The favourable facts are real: the Service Manager holds write authority
(agents never write staffing outcomes), the proposal sits in `pending` until a human moves it, and
`PackageJson` hands the approver the whole evidence base rather than a bare score — which is
precisely the "consider all the relevant data" condition. The unfavourable facts are equally real:
the exhaustive roster scan produces an ordered `MatchScore`/`Rank` that the approver did not
compute and cannot easily reconstruct, and if in practice approvals track the top-ranked candidate,
SCHUFA §50's "determining role" is satisfied and the *scoring* becomes the decision no matter what
the UI says. **This is a fact question about observed behaviour, not a question the sources settle.**

The ICO has now studied exactly this pattern in the field and come down against it — including the
point that the approver reading the shortlist does nothing for the Experts the pipeline scored *out*
of it. That is in [§8](#8-national-dpa-guidance-ico-and-cnil-do-not-agree), and it is the single
finding in this file most likely to force a change to the pipeline rather than to a privacy notice.

The second condition is also unsettled for us. "Similarly significantly affects" is read narrowly —
WP251 says "only serious impactful effects will be covered". Not being proposed for one engagement
from an internal bench is a weaker effect than a refused loan or a rejected job application. A
recurring, systematic exclusion from all opportunities is a stronger one. This distinction is worth
designing for: never scoring an Expert *out* of visibility permanently is the cheapest way to keep
the effect below the threshold.

If Art. 22 does apply, **C-203/22 Dun & Bradstreet Austria** fixes what Art. 15(1)(h) then owes:
the controller must "describe the procedure and principles actually applied in such a way that the
data subject can understand which of his or her personal data have been used in what way" (§61),
and those requirements "cannot be satisfied either by the mere communication of a complex
mathematical formula, such as an algorithm, or by the detailed description of all the steps" (§59).
Trade secrecy is not a refusal ground: the controller "is required to provide the allegedly
protected information to the competent supervisory authority or court, which must balance the rights
and interests at issue" (§76). In our terms: the model name, the prompt's criteria and the band
definitions, not the weights and not a dump of the chain.

## 7. Special categories (Art. 9)

Art. 9(1) prohibits processing of data "revealing racial or ethnic origin, political opinions,
religious or philosophical beliefs, or trade union membership", genetic and biometric data, "data
concerning health" and sex-life/sexual-orientation data, unless an Art. 9(2) condition applies.

**Free-text CV fields do create Art. 9 exposure, and the CJEU's test is deliberately wide.** In
**C-184/20 (OT v Vyriausybės ekstremaliųjų situacijų komisija)** the Court held that publication of
data "liable to disclose indirectly the sexual orientation of a natural person" is processing of
special categories (operative part, §128), reasoning that the verb "revealing" covers information
obtained by "an intellectual operation involving comparison or deduction" (§§122–124), and that "a
wide interpretation of the terms 'special categories of personal data' and 'sensitive data'" follows
from the objective of the GDPR (§125), because otherwise the regime's effectiveness "would be
compromised" (§127).

WP251 rev.01 (p. 15) applies the same logic to profiling: special-category rules cover "special
category data **derived or inferred** from profiling activity", because "profiling can create
special category data by inference from data which is not special category data in its own right but
becomes so when combined with other data". Its instruction to controllers, where sensitive
characteristics are inferred, is threefold: the processing must not be incompatible with the
original purpose; a lawful basis for the special-category processing must be identified; and the
data subject must be informed.

In practice, for a CV bench: a summary line about a career gap for treatment, a "union
representative" achievement bullet, a religious charity role, a nationality or a photograph in an
uploaded document all put Art. 9 material into free text that no schema controls — and our
embedding then encodes it and makes it semantically retrievable, which is a stronger form of
processing than mere storage.

**What is a controller expected to do?** The sources set the duty but not the recipe, so be honest
about the gap:

- Art. 9(2)(b) (employment law obligations) needs authorisation "by Union or Member State law or a
  collective agreement" — a private staffing bench does not have that by default.
- Art. 9(2)(e) (data "manifestly made public by the data subject") does not cover a CV uploaded
  privately to us; it is a narrow exception and reading it broadly would be aggressive.
- That leaves **Art. 9(2)(a) explicit consent** as the only realistic condition — which pulls
  against a legitimate-interest roster and is a real design tension, not a detail.
- The alternative is to keep Art. 9 data out of the *purpose*: do not build features that select,
  rank or filter on inferred sensitive signals, minimise at intake, and be able to show it. That
  does not make incidental storage disappear; it changes what the balancing test looks like.

The ICO goes further than the EU sources here and states the inference rule flatly — inferring a
protected characteristic from an application "is processing special category information… **whether
your inference about a candidate is correct or not**" — and warns against exactly the feature shape
we own, having found recruitment tools with "a search functionality that allowed recruiters to
filter out candidates with certain protected characteristics"
([§8](#8-national-dpa-guidance-ico-and-cnil-do-not-agree)).

The sources do **not** settle whether merely storing unfiltered CV free text — with no attempt to
use the sensitive parts — constitutes Art. 9 processing. C-184/20's "liable indirectly to reveal"
test suggests it can. This is the single most DPO-shaped question in the file.

## 8. National DPA guidance: ICO and CNIL do not agree

Two health warnings before any of this is used. The **ICO's recruitment guidance is still a draft**
("Our consultation on this draft guidance is now closed. The final version will be published in due
course"), and it is **UK law**, which after the Data (Use and Access) Act no longer tracks the EU
GDPR — most visibly, UK Art. 22A now states in statute that "a decision is based solely on
automated processing if there is no **meaningful human involvement** in the taking of the decision".
Treat the ICO material as the best available regulator *reasoning* about our exact product shape,
not as EU law.

### They give opposite answers for a CV pool

| | ICO (UK, draft) | CNIL (FR) |
|---|---|---|
| Recruitment process itself | Legitimate interests; consent "unlikely to be an appropriate lawful basis" given the imbalance of power | Agrees: cannot rest on consent, "dès lors qu'un refus de leur part pourrait affecter leurs chances d'obtenir un emploi" |
| **Keeping CVs for future roles (our bench)** | **Consent is prohibited**: "You **must not** rely on consent in order to consider a candidate for multiple roles, or future roles, as their consent will not be specific, granular and informed… recruitment agencies are most likely to rely on the legitimate interests basis" | **Consent** — "les traitements peuvent se fonder sur la base légale du consentement du candidat", explicitly reversing CNIL's own earlier legitimate-interest recommendation. The Apr/May 2026 *référentiel* softens this to consent **or** legitimate interest with an easy opt-out |
| Staffing/temp agencies specifically | Not addressed | **Pre-contractual measures, Art. 6(1)(b)**: "lorsqu'un candidat s'inscrit dans une agence d'intérim et intègre un vivier de candidats, cette opération peut être considérée comme un préalable à l'éventuelle relation contractuelle future" |
| Retention for the pool | No number: "it does not specify timescales". Anchor is the limitation period for claims arising from the process | **2 years from last contact** (a *recommandation*), renewable; plus **5 years** intermediate archive from the date the post was filled for discrimination-claim defence (Art. L.1134-5 Code du travail — the only figure marked as an obligation) |

**There is no single lawful basis that satisfies both regulators for the same bench.** If we operate
in more than one jurisdiction, per-Expert basis configuration is not over-engineering, it is the
minimum. CNIL's *agences d'intérim* carve-out is the most favourable route found anywhere in this
research for a consultant bench — it is Art. 6(1)(b), so it **preserves the Art. 20 export right**,
and CNIL adds that the data "peuvent être maintenues en base active y compris après la décision de
placement" with the candidate "libre de demander le retrait de son dossier à tout moment". Whether a
SaaS bench of consultants qualifies as the French concept is unresolved.

### The ICO on human involvement — this is the finding that most threatens our design

The ICO's *Recruitment rewired* report (March 2026, 30+ employers, fieldwork Mar 2025–Jan 2026)
examined precisely the architecture we have built:

> **We found that most employers thought their use of automated recruitment tools constituted
> decision support rather than decision-making** … However, the evidence we saw indicated that, in
> practice, employers were using the tools to make solely automated decisions.

It names the failure mode: employers "could not consistently demonstrate how they had mitigated the
risk of their hiring managers **relying disproportionately on the scores** when faced with a high
volume of applications", with lower-scoring candidates getting a "'rubber-stamping' of the score"
or no intervention at all. And it states the rule that decides our case:

> **Meaningful human involvement can work as its own safeguard, but it must be applied to every
> candidate, not just those who score highly.**

Its worked example of what *does* qualify: a recruiter who "goes through **all** the scores and
profiles, including the responses… They also have access to additional factors in making their
decision, such as CVs. They then make a final decision about whether to progress or reject each
candidate."

Two corollaries that bite directly:

- **Configuring the model is not human involvement.** "humans… participate in designing and
  configuring the models that generate fit scores. This alone is not enough to constitute meaningful
  human involvement… the design phase happens long before any real-world decisions are made about
  people."
- **The asymmetry is the problem.** Our exhaustive roster scan scores everyone; the Service Manager
  sees a ranked proposal. Every Expert the pipeline scored *out* of the shortlist received a purely
  automated outcome. Under the ICO's rule that is the population that puts us in scope, not the
  handful the approver actually reads.

The ICO's binary: employers must "either apply the safeguards with the ADM provisions; or adapt
their organisational processes to ensure that there is meaningful human involvement in each decision
about each candidate."

### The ICO on inferred special-category data

From the Nov 2024 *AI tools used in recruitment* audit outcomes (296 recommendations, 42 advisory
notes, 97% accepted):

> Others **estimated or inferred people's gender, ethnicity, and other characteristics from their
> job application or even just their name**, rather than asking candidates directly. This inferred
> information is not accurate enough to monitor bias effectively. **It was often processed without a
> lawful basis and without the candidate's knowledge.**

And from the draft recruitment guidance, the rule in its cleanest form:

> Where such an inference is being made about a candidate, **you are processing special category
> information. This is the case whether your inference about a candidate is correct or not.**

The audit also found tools with "a **search functionality that allowed recruiters to filter out
candidates with certain protected characteristics**" — a direct warning about what our semantic
search must not become. And the bias-monitoring bind has no clean exit: the ICO says "**Inferred or
estimated data will not be adequate and accurate enough, and will therefore not comply with data
protection law**" for that purpose, so the only compliant route to bias monitoring is asking people
voluntarily.

### Smaller ICO points that are still design constraints

- **A DPIA is required**: "you must do [a DPIA] if you have changed your processes in ways that are
  likely to result in a high risk to candidates. For example, **if you decide to use AI software to
  help you make recruitment decisions about candidates**."
- **The LIA**: "To demonstrate that legitimate interests applies, **you must do the three-part
  test. You should document the outcome.**" The UK GDPR "doesn't require you to do an LIA. But you
  should do one anyway." Usefully, one assessment can cover the cohort: "You are not required to
  carry out a separate legitimate interests assessment for each candidate."
- **Unwanted CVs**: "If you receive information about a candidate but you do not wish to consider
  them for a vacancy… **you must delete their information as soon as possible.**"
- **Controllership**: an AI provider "is the controller if it exercises overall control of the means
  and purpose of processing in practice. For example, **if it uses the personal information it
  processes on the recruiter's behalf to develop a central AI model** that they deploy to all
  recruiters." Relevant to how we contract with model providers and to any future fine-tuning on
  Expert data.
- **Anonymisation is not stated to satisfy erasure.** The ICO's right-to-erasure page never mentions
  anonymisation; its "beyond use" concession there is about *backups* only ("put the backup data
  'beyond use', even if it cannot be immediately overwritten"). The anonymise-or-delete framing
  lives on the storage-limitation page and answers the different question of what to do with data
  you no longer need. CNIL, by contrast, does say outright that expired archives must be
  "supprimées ou anonymisées". So our §3 conclusion holds on the EU sources and the ICO does not
  contradict it — but nobody should claim the ICO has *endorsed* anonymise-instead-of-delete.
- **France, labour law, not data protection**: "vous devez également penser à **informer le comité
  social et économique (CSE) avant d'utiliser ou de modifier des méthodes ou techniques d'aide au
  recrutement**." Deploying AI matching for a French client has a works-council gate before rollout.

## 9. What actually needs a DPO, not us

Stated plainly, because papering over these with a confident paraphrase would be worse than useless:

1. **Whether unfiltered CV free text plus semantic retrieval is Art. 9 processing**, and if so which
   Art. 9(2) condition a private staffing bench can actually rely on. C-184/20 pushes toward yes;
   nothing in the guidance tells a bench operator what to do about it.
2. **Whether the Service Manager's approval is "meaningful human involvement"** under WP251 given
   SCHUFA §50. This turns on observed approval behaviour — override rates, whether approvers depart
   from rank order — which we can measure but cannot self-certify.
3. **Whether being passed over for staffing is a "similarly significant effect"** at all. WP251
   says only serious effects count; the line for an internal bench is undrawn.
4. **Whether the proposal ledger's retention survives Art. 17(3)(e)** and for how long. The
   necessity scoping is a legal judgement about claim-limitation periods in the jurisdictions we
   operate in.
5. **Which jurisdictions we are in, and therefore which basis applies where.** The ICO and the CNIL
   conflict on the lawful basis for a CV pool ([§8](#8-national-dpa-guidance-ico-and-cnil-do-not-agree)),
   and CNIL's *agences d'intérim* Art. 6(1)(b) route — the most favourable one found — was written
   for French temp agencies, not for a consultant SaaS. Whether we can stand on it is a legal call.
6. **Whether a DPIA is mandatory.** Art. 35(3)(a) (systematic and extensive evaluation of personal
   aspects based on automated processing) plus the ICO's own "you must [do a DPIA]… if you decide to
   use AI software to help you make recruitment decisions" makes it very likely; the formal call and
   the assessment itself belong to a DPO.
7. **Retention periods.** The ICO refuses to give a number; CNIL's 2 years from last contact is a
   *recommandation* and its 5-year archive is grounded in the French Code du travail. Porting either
   to our jurisdictions is a legal judgement, and it is the input the erasure ticket most needs.

## 10. Verdict

| # | Obligation | Verdict | Finding | Authority |
|---|---|---|---|---|
| 1 | Lawful basis for the roster | **we-must-decide** | Consent and legitimate interest are both available for self-registration; only Art. 6(1)(f) is available when a Service Manager creates the row. The basis must be picked and recorded **per Expert, before collection** | Art. 6(1)(a)/(f); Recital 47; EDPB GL 1/2024 §§6, 9, 17 |
| 1b | Lawful basis for a *bench* specifically | **we-must-decide, per jurisdiction** | The regulators conflict. ICO: consent is **prohibited** for considering a candidate for future roles — use legitimate interests. CNIL: consent (or LI with easy opt-out) for a CVthèque, and **Art. 6(1)(b) pre-contractual measures** for staffing agencies. No single basis satisfies both | ICO recruitment guidance (draft); CNIL *Guide recrutement* 2023 + *référentiel* 2026 |
| 2 | No retro-fitting the basis | **required** | A controller "cannot swap from consent to other lawful bases"; a forward change must be notified under Arts. 13/14 | EDPB GL 05/2020 §§120, 123 |
| 3 | Art. 7 consent mechanics | **required (if consent is chosen)** | Granular per purpose, withdrawable in the same interface, no detriment or reduced service on withdrawal, and provably given | Art. 7(1)–(4); EDPB GL 05/2020 §§43–48, 113–115 |
| 4 | Delete on consent withdrawal | **required (if consent is chosen)** | Data processed on withdrawn consent must be deleted absent another purpose with its own basis | Art. 17(1)(b); EDPB GL 05/2020 §§117, 119 |
| 5 | Erasure reaches derived stores | **required** | `ScoringJobCandidate` and `StaffingProposalCandidate` have no FK to `Employee` and survive deletion today, carrying `Name`, `Digest`, `Rationale`. `PackageJson` holds a full CV dump | Art. 17(1); `AppDbContext.cs` |
| 6 | Keeping the proposal decision record | **permitted, narrowly** | Art. 17(3)(e) can justify keeping *that a decision was made and by whom*, scoped by necessity. It does not justify keeping the CV dump, and there is no audit-trail exception. Art. 18 restriction is the better-fitting mechanism | Art. 17(3)(e); Art. 18(1)(c), 18(2) |
| 7 | Scrub-and-keep as erasure | **not sufficient** | Keeping ids and scores is pseudonymisation, still personal data, still erasable. Deleting the `Employee` row does not make the residue anonymous — anonymity must be demonstrated | Art. 4(5); Recital 26; EDPB GL 01/2025 §22 |
| 8 | CV chunks are personal data | **required to treat as such** | `Content` is the Expert's own text stored against their id | Art. 4(1) |
| 9 | Embeddings are personal data | **required to treat as such** | A per-person vector keyed to `EmployeeId` singles out one person. Opinion 28/2024 forecloses the "just numbers" defence: models designed to make training-subject data available "cannot be considered anonymous", data can "remain 'absorbed' in the parameters", and anonymity is a documented accountability claim | Recital 26; EDPB Op. 28/2024 §§29, 31, 43, 56–58 |
| 10 | Portability right exists at all | **conditional** | Only under consent or contract. Under legitimate interest there is **no Art. 20 duty** — voluntary export is good practice | Art. 20(1)(a); Recital 68; WP242 p. 8 |
| 11 | Portability covers derived data | **not required** | Scores, digests, rankings, embeddings and profiles are "created by the data controller" and out of scope; the export must actively filter them out | WP242 pp. 9–11, 16 |
| 12 | Portability format | **required (if Art. 20 applies)** | Structured, commonly used, machine-readable, interoperable. JSON qualifies; **our rendered CV PDF does not** | Art. 20(1); Recital 68; WP242 p. 16 |
| 13 | Direct controller-to-controller transfer | **not required** in practice | Only "where technically feasible"; no duty to adopt compatible systems | Art. 20(2); Recital 68 |
| 14 | Access view field list | **required** | Purposes, categories, recipients (incl. the embedding/scoring model provider), retention period *or the criteria*, rights, complaint right, **source** where not collected from the Expert, and Art. 22 logic. Plus a copy of the data | Art. 15(1)(a)–(h), 15(3) |
| 15 | Access covers derived data | **required** | Inferred and derived data — "algorithmic results", rankings, scores, digests — are in scope for access even though they are out of scope for portability | EDPB GL 01/2022 §§97, 99, Example 16 |
| 16 | Self-service access surface | **permitted** | Accepted delivery mechanism, provided it does not narrow the data returned and out-of-band requests are still honoured | EDPB GL 01/2022 §§137–138 |
| 17 | Human approval escapes Art. 22 | **we-must-decide — and currently at risk** | Not automatic. Oversight must be meaningful, by someone with authority and competence, considering all relevant data; where the score "plays a determining role" the scoring is itself the decision. The ICO field-studied this exact pattern and found most such employers were in fact making solely automated decisions | SCHUFA C-634/21 §§43, 50, 61; WP251 rev.01 p. 21; Recital 71; ICO *Recruitment rewired* (2026) |
| 17b | Human involvement must reach **every** Expert | **required, if Art. 22 is in play** | "Meaningful human involvement… must be applied to every candidate, not just those who score highly." Our approver reads a shortlist; everyone the scan scored *out* got a purely automated outcome. Configuring the model does not count | ICO *Recruitment rewired* — Meaningful human involvement |
| 18 | Explaining the logic if Art. 22 applies | **required** | "The procedure and principles actually applied" so the subject understands which data were used how — not the algorithm, not every step. Trade secrets go to the SA/court, they are not a refusal ground | C-203/22 §§59, 61, 76; Art. 15(1)(h) |
| 19 | Art. 9 exposure from free text | **required to address** | "Revealing" covers indirect disclosure by comparison or deduction, and profiling can *create* special-category data by inference. Controllers must not process incompatibly, must identify an Art. 9(2) condition, and must inform | Art. 9(1); C-184/20 §§122–128; WP251 rev.01 p. 15 |
| 19b | Inferring protected characteristics | **not permitted without a condition** | An inference of gender/ethnicity from an application or even a name "is processing special category information… whether your inference about a candidate is correct or not", and inferred data is not accurate enough to be lawful for bias monitoring. Never build search that filters on such signals | ICO *AI tools used in recruitment* (Nov 2024); ICO recruitment guidance (draft) |
| 20 | Documenting the anonymity/LI reasoning | **required** | Anonymity claims and legitimate-interest reliance are both accountability claims that must be documented before processing, not asserted after. The ICO expects a written LIA covering the three-part test — one assessment can cover the whole cohort | Art. 5(2); EDPB Op. 28/2024 §§56–58; EDPB GL 1/2024 §§6–7; ICO LIA guidance |
| 21 | DPIA | **required (very likely)** | Systematic evaluation of personal aspects by automated processing; the ICO says a DPIA must be done "if you decide to use AI software to help you make recruitment decisions about candidates" | Art. 35(1), 35(3)(a); ICO recruitment guidance (draft) |
| 22 | Retention periods | **we-must-decide** | The ICO gives no number and anchors to claim-limitation periods; CNIL recommends **2 years from last contact** for a CV pool (renewable) plus a **5-year** restricted archive for discrimination-claim defence. Neither transfers automatically to our jurisdictions | Art. 5(1)(e); ICO *Keeping recruitment records*; CNIL *Guide recrutement* / *référentiel* |
| 23 | Deleting CVs we will not consider | **required** | "If you receive information about a candidate but you do not wish to consider them for a vacancy… you must delete their information as soon as possible" | ICO recruitment guidance (draft) |

## Sources

All read directly; none via secondary commentary.

- **Regulation (EU) 2016/679 (GDPR)**, consolidated text — [eur-lex CELEX:32016R0679](https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:32016R0679) (**primary**: Arts. 4, 6, 7, 9, 15, 17, 18, 20, 21, 22; Recitals 26, 47, 68, 71)
- **EDPB Guidelines 05/2020 on consent** — [PDF](https://www.edpb.europa.eu/system/files/documents/files/file1/edpb_guidelines_202005_consent_en.pdf)
- **EDPB Guidelines 1/2024 on processing based on Art. 6(1)(f)** — [PDF](https://www.edpb.europa.eu/system/files/2024-10/edpb_guidelines_202401_legitimateinterest_en.pdf)
- **EDPB Guidelines 01/2022 on data subject rights — right of access** (v2.0) — [PDF](https://www.edpb.europa.eu/system/files/2023-04/edpb_guidelines_202201_data_subject_rights_access_v2_en.pdf)
- **EDPB Guidelines 01/2025 on Pseudonymisation** — [PDF](https://www.edpb.europa.eu/system/files/2025-01/edpb_guidelines_202501_pseudonymisation_en.pdf)
- **EDPB Opinion 28/2024 on personal data processing in the context of AI models** — [PDF](https://www.edpb.europa.eu/system/files/2024-12/edpb_opinion_202428_ai-models_en.pdf)
- **WP29 Guidelines on the right to data portability, WP242 rev.01** (endorsed by the EDPB) — [PDF](https://ec.europa.eu/newsroom/dae/redirection/document/44099) · [landing page](https://ec.europa.eu/newsroom/article29/item-detail.cfm?item_id=611233)
- **WP29 Guidelines on Automated individual decision-making and Profiling, WP251 rev.01** (endorsed by the EDPB) — [PDF](https://ec.europa.eu/newsroom/article29/redirection/document/49826) · [landing page](https://ec.europa.eu/newsroom/article29/items/612053/en)
- **CJEU C-634/21, SCHUFA Holding (Scoring)**, 7 Dec 2023 — [eur-lex CELEX:62021CJ0634](https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:62021CJ0634)
- **CJEU C-203/22, Dun & Bradstreet Austria** — [eur-lex CELEX:62022CJ0203](https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:62022CJ0203)
- **CJEU C-184/20, OT v Vyriausybės ekstremaliųjų situacijų komisija** — [eur-lex CELEX:62020CJ0184](https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:62020CJ0184)
- **ICO — Employment practices: recruitment and selection** (*draft*, under DUAA review) — [landing](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/employment/recruitment-and-selection/) · [Data protection and recruitment](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/employment/recruitment-and-selection/data-protection-and-recruitment/) · [Keeping recruitment records](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/employment/recruitment-and-selection/keeping-recruitment-records/)
- **ICO — AI tools used in recruitment: audit outcomes** (Nov 2024) — [report](https://ico.org.uk/action-weve-taken/audits-and-overview-reports/2024/11/ai-tools-used-in-recruitment/) · [PDF](https://ico.org.uk/media2/migrated/4031620/ai-in-recruitment-outcomes-report.pdf)
- **ICO — Recruitment rewired** (Mar 2026) — [report](https://ico.org.uk/about-the-ico/what-we-do/recruitment-rewired/) · [Meaningful human involvement](https://ico.org.uk/about-the-ico/what-we-do/recruitment-rewired/key-findings-how-are-employers-automating-their-recruitment-processes/meaningful-human-involvement/)
- **ICO** — [Right to erasure](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/individual-rights/individual-rights/right-to-erasure/) · [Storage limitation](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/data-protection-principles/a-guide-to-the-data-protection-principles/storage-limitation/) and [Legitimate interests / LIA](https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/lawful-basis/legitimate-interests/how-do-we-apply-legitimate-interests-in-practice/)
- **CNIL — Guide/référentiel recrutement** (30 Jan 2023) — [PDF](https://www.cnil.fr/sites/cnil/files/atoms/files/guide_referentiel_recrutement.pdf) · [TPE/PME fact sheet](https://www.cnil.fr/fr/recrutement-et-donnees-personnelles-dans-les-tpepme-cinq-questions-incontournables-se-poser) · [Référentiel durées de conservation RH (2026)](https://www.cnil.fr/sites/default/files/2026-04/referentiel_durees_de_conservation_gestion_des_ressources_humaines.pdf)
- Code read for the data-model characterisation: `api/Infrastructure/Persistence/AppDbContext.cs`,
  `api/Infrastructure/Persistence/EmployeeSearchChunk.cs`, `api/Domain/Entities/ScoringJob.cs`,
  `api/Domain/Entities/StaffingProposal.cs`
