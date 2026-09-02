# What we hold on you, and the copy you can take away

> **Status (2026-09-02):** shipped as P1T-187. `GET /api/me/access` is the Art. 15 view,
> `GET /api/me/export` the Art. 20 copy, and `POST /api/experts/{id}/export` the Service Manager's
> on-behalf export, which writes its own record. Decision:
> [P1T-174](https://linear.app/p1t0ns-nest/issue/P1T-174). The store declaration both read is
> `manuals/personal-data-and-erasure.md`.

## 1. Two surfaces, because they owe opposite things

Art. 15 access covers data **derived about** somebody; Art. 20 portability covers only what they
**provided**. So the export has to filter out precisely what the access view has to include. One
endpoint with a flag would eventually get one of the two wrong, so there are two.

| | Access view | Export |
| --- | --- | --- |
| Owed to | everyone, including legitimate-interest records | 6(1)(b) records; offered to LI as a courtesy |
| The record itself | yes | yes, identical |
| Basis history | yes | yes |
| Scores, bands, rationales, digests, match answers | **yes** | **no** |
| Art. 15 disclosure text | yes | no — it is not their data |

`TransparencyTests.Derived_data_is_in_the_access_view_and_out_of_the_export` proves both directions
in one test, because a change that broke the pair would otherwise pass half of it.

## 2. The person reads what the software wrote about them

Scores, bands, `Rationale`, `Digest` and the model's `Match.Answer` are all in the access view. This
is owed (EDPB GL 01/2022 §§97–99), and Art. 22(3)'s right to contest a decision is meaningless if
the person cannot see what they would be contesting.

**Treat that as a constraint on the pipeline, not a disclosure chore: the rationale has to be
defensible, because its subject will read it.** That is a healthier pressure on what a model is
allowed to write than secrecy would be.

## 3. The label moves, the payload does not

Art. 20 is owed to a record on 6(1)(b) and to nobody else. We hand over the same file either way and
change only the word for it — `Right` or `Courtesy`, with a sentence saying which and why.

Building a basis check whose only job is to **deny** a file we are happy to give would be worse than
useless, and one behaviour stays truthful by itself when an approved claim moves a record from
legitimate interest to contract necessity. The test asserts the label flips and the payload does
not — except the basis history, which legitimately grows by exactly the transition that flipped it.

## 4. Recipients are categories, and one of them is new information

Art. 15(1)(c) permits categories, and this service states three: Service Managers, clients it puts
people forward to, and **Google (Gemini) as the model provider**.

The third is not a restatement. Until this slice the service named its model provider to nobody
while sending every CV to it.

> **A gap this slice does not close.** The transparency notice's own "Who sees it" section still
> omits the model provider, and Art. 13(1)(e) asks for recipients *at collection*. Closing it means
> publishing a new notice version, which changes what every existing account has acknowledged — a
> deliberate act with its own consequences, not something to slip into this slice. Recorded here so
> it is a known gap rather than an oversight.

**No read log.** Nothing records who viewed whose record. Answering a disclosure duty by
manufacturing a large new store of personal data about access would then need its own disclosure,
retention and erasure — the cure being the disease.

## 5. The on-behalf export is not a read log

Somebody phones in and asks for their data, because there is no email to ask by. A Service Manager
takes it from the expert's page, and **that act writes a `DataExportRecord`**: who took whose file,
when.

Three things keep this on the right side of §4's rule. It records one deliberate act of extracting a
person's complete file, not a view. It is a fact **about the Service Manager**. And a person
exporting their own data writes nothing at all — `A_service_manager_export_writes_its_own_record_and_a_self_export_does_not`
asserts both halves, including that merely opening the record or its CV still writes nothing.

The record is declared in `PersonalDataDeclaration` and cascades with the Expert: after erasure there
is no file to have taken.

## 6. Art. 15(1)(h): the logic, conceded rather than dressed up

`Art15Disclosure.Art22Logic` describes the procedure and principles **actually applied**
(C-203/22 §§59, 61, 76) — retrieval, then assessment by a model that returns a score, a band and a
rationale — and says plainly that the ranking decides who a Service Manager sees, and therefore who
is considered.

It does **not** claim meaningful human review. We rely on Art. 22(2)(a), so conceding the automation
is the honest position and the safeguards are what have to earn it. It also states the two things
the person can act on: they read the rationale, and they can ask for a human to look again.

## 7. Retention: the criterion, and now the date

`Art15Disclosure.Retention` gives the criterion. Since P1T-188 the access view also carries the
person's **own expiry date** and which clock they are on, computed by the same `RetentionPolicy` the
sweep runs — so the date somebody is shown is the date their record actually goes, rather than a
description and a behaviour that can drift apart. Inside the final thirty days a banner renders, and
reading it is itself activity. See `manuals/retention.md`.

## 8. Where the surfaces are, and what is not built yet

`Art15Disclosure` is separate from `TransparencyNotice` on purpose: the notice is a versioned
artefact somebody acknowledged at a moment in time, and every version of it stays readable forever.
This is a description of the service as it stands now — versioning "how things are today" answers
the wrong question.

The Expert-facing **page** that renders all of this is P1T-191; this slice ships the endpoints, the
Service Manager's export button, and the API module the page will use. `useMyAccessView` and
`useDownloadMyExport` are there and unrendered until then.
