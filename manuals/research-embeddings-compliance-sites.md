# Which compliance artifacts name Google as the embeddings recipient? (audit)

Audit of this repo at `main` (`86850c7`), 2026-09-27, for EXP-55 under the map EXP-52
(*embeddings on Azure OpenAI — Google out, provider selected by configuration*). Template:
[`provider-naming-compliance-audit.md`](provider-naming-compliance-audit.md) (EXP-7, the chat map's
equivalent).

**The question.** On an Azure-embeddings deployment Google should stop receiving career narratives.
Which runtime strings and documents name Google/Gemini as the **embeddings** recipient, or assume
embeddings always go to Google — and would changing them force every account to re-acknowledge the
transparency notice?

**Headline.**

- **31 sites** name Google/Gemini as the embeddings recipient or encode "embeddings never move".
  **13 are code** (5 served strings, 4 plumbing/config, 4 tests) **and 18 are prose.** The served
  strings come down to **two surfaces a data subject reads**: the Art. 15(1)(c) recipient list (`Art15Disclosure.RecipientsFor`) and the
  search-index note on the privacy page (`AccessAndExportService.cs:283-286`).
- **The chat half is config-derived; the embeddings half is not.** EXP-21 made the recipient list
  follow `Ai:Chat:Provider`, but it has only one input: Google-for-embeddings is implied by *every*
  branch, including the "unknown provider" fallback. The search-index note is a bare literal
  ("produced by Google's Gemini models") with no configuration behind it at all.
- **Consent impact: none required.** `TransparencyNotice` names no provider and never mentions
  embeddings, so nothing in it goes false and `CurrentVersion` need not move. Even if it were moved,
  a bump **notifies, it does not gate**: Users only, a non-blocking banner, no data withheld
  (`TransparencyNotice.cs:55-68`, `NoticeController.cs:66-70`). The standing Art. 13(1)(e) gap
  (notice omits the model provider) is unchanged by this effort.
- **Pre-existing finding, not caused by this effort:** the Web host's `Ai:Chat:Provider` is still
  `Gemini` (`api/Web/appsettings.json:11`) while the Agents host ships `AzureFoundry`
  (`api/Agents/appsettings.json:10`, EXP-41). The AppHost sets neither for the Web host. So on the
  default stack **the privacy page today says "Google (Gemini), as our AI model provider" and does
  not name Microsoft at all**, although chat goes to Azure. This deserves its own issue.
- **A design question the ADR must settle:** Art. 15(1)(c) GDPR covers recipients to whom data
  "have been or will be disclosed". A deployment that switches embeddings from Gemini to Azure has
  *already* sent every existing narrative to Google. Dropping Google from the access view the
  moment the config flips would understate past disclosure to those people. The repo does not
  record when a deployment switched, so this cannot be derived today. Options are listed in §4.

Sites were found by grepping `google|gemini|embedd` (case-insensitive) across
`api/Application/Compliance/*`, `api/Web`, `api/Mcp`, `api/AppHost`, `web/src`, `web/e2e`, `tests/`,
every file in `manuals/`, `README.md`, `CLAUDE.md`, and `CONTEXT.md`. A second sweep looked for
`recipient | sub-processor | third country | transfer` in the compliance manuals. Hits about model
ids for chat, UI fonts or retrieval benchmarks were dropped.

---

## 1. Runtime and test sites

"Config-derived?" asks whether the Google-for-embeddings claim follows configuration today. It never
does. Only the chat half is derived.

### 1a. Strings a data subject reads (served by `GET /api/me/access`)

| # | File : line | What it says now | Config-derived? | Embeddings on **Azure** | Embeddings on **Gemini** |
| --- | --- | --- | --- | --- | --- |
| R1 | `api/Application/Compliance/Art15Disclosure.cs:66-69` | `RecipientsFor(DisclosedChatProvider?)`: `Gemini` → `[Administrators, GoogleForEverything, Clients]`; anything else (incl. `null`) → `[Administrators, GoogleForEmbeddings, MicrosoftForChat, Clients]` | Chat: yes (EXP-21). Embeddings: **no**. Google is in every branch | Needs a second input (the embeddings provider). Chat Azure + embeddings Azure → a **single Microsoft entry doing both**, no Google. Chat Gemini + embeddings Azure → Google for scoring, Microsoft for embeddings | Unchanged: today's two branches |
| R2 | `Art15Disclosure.cs:83-87` (`GoogleForEverything`) | "Google (Gemini), as our AI model provider" / "sent to Google's Gemini models to be turned into search embeddings and to be scored against job descriptions…" | Selected by chat config | Not used | Used when chat is also Gemini. Keep word for word (its own doc comment at `:81-82` promises that) |
| R3 | `Art15Disclosure.cs:89-93` (`GoogleForEmbeddings`) | "Google (Gemini), as our embeddings provider" / "…turned into search embeddings, so that a search for a capability can find your record…" | Selected by chat config only | Not used, unless §4 keeps a "formerly" entry | Used when chat is Azure |
| R4 | `Art15Disclosure.cs:95-99` (`MicrosoftForChat`) | "Microsoft (Azure OpenAI), as our AI model provider" / "Your career narrative, your skills and your availability are sent to Microsoft's Azure OpenAI service **to be scored** against job descriptions…" | Selected by chat config | With both on Azure, the wording must also say the narrative is **turned into search embeddings** there, the counterpart of `GoogleForEverything`. That means a new `MicrosoftForEverything` rather than editing this one. With chat on Gemini a new `MicrosoftForEmbeddings` is needed | Used as today when chat is Azure |
| R5 | `api/Application/Compliance/AccessAndExportService.cs:283-286` (`DerivedDataDto.SearchIndexNote`) | "Your summary and each of your roles are also held as numeric representations (embeddings) **produced by Google's Gemini models**, so that a search for a capability can find your record…" | **No, a bare literal.** `AccessAndExportService` takes `ChatProviderDisclosure` (`:144`) but this string ignores it. Rendered verbatim at `web/src/pages/PrivacyDataPage.tsx:221` ("The search index" row) | "…produced by Microsoft's Azure OpenAI service…" | Unchanged |

`PrivacyDataPage.tsx` holds no provider text of its own. Both R1 and R5 are server-driven, so the
page needs no change. The JSON export (`BuildExportAsync`, `AccessAndExportService.cs:193-226`)
carries neither recipients nor the search-index note, so it needs no change either.

### 1b. The plumbing that decides what R1/R5 say

| # | File : line | What it does now | What changes |
| --- | --- | --- | --- |
| R6 | `api/Application/Compliance/ChatProviderDisclosure.cs:12-19` | `DisclosedChatProvider { Gemini, AzureFoundry }`. The doc comments say "Gemini serves chat, and — as always — embeddings too" and "embeddings stay on Google" | Needs a sibling axis (e.g. a disclosed embeddings provider) parsed the same way, with the same "unrecognised → null → name every candidate" rule (`:25-30`). The comments encode the assumption being removed |
| R7 | `api/Web/Program.cs:55-60` | Registers `ChatProviderDisclosure.From(configuration["Ai:Chat:Provider"])` in the **Web** host | The embeddings provider has to be readable in the Web host too. Embeddings run in the **MCP** host (`api/Mcp/appsettings.json:11-17`, `Ai:Gemini:EmbeddingModel`), so the Web host sees that key only if it is set there as well. This is the same shape of drift R8 already shows for chat |
| R8 | `api/Web/appsettings.json:9-13` | `Ai:Chat:Provider = "Gemini"`. The Agents host says `AzureFoundry` (`api/Agents/appsettings.json:9-10`, changed by EXP-41 commit `110896b`, which did not touch the Web file). The AppHost sets no `Ai__Chat__Provider` for the Web host | **Pre-existing misdisclosure** on the default stack (see Headline). Whatever key the embeddings seam introduces must not repeat this: one source for both hosts, or a test that the two hosts agree |
| R9 | `api/Application/DependencyInjection.cs:48` | `TryAddSingleton(new ChatProviderDisclosure(null))` fallback | Same fallback is needed for the embeddings axis |

### 1c. Tests that freeze "embeddings are Google"

| # | File : line | What it asserts | What changes |
| --- | --- | --- | --- |
| T1 | `tests/Application.Tests/Art15RecipientDisclosureTests.cs:11-37, 52-59` | Under Gemini, one entry names Google. Under Azure, a Google entry says "embeddings provider". An unrecognised value still names Google | Becomes a chat × embeddings matrix. The assertions that name Google for embeddings hold only when the embeddings provider is Gemini. Keep the `outside this company` floor for every entry |
| T2 | `tests/Web.Tests/TransparencyTests.cs:57, 71-84` | `The_access_view_names_the_embeddings_provider_separately_under_azure`: an entry containing "embeddings provider" contains `Google` | Parameterise on the embeddings provider. With both on Azure there is **no** separate embeddings entry, so the `Single(...)` would throw. That is a correct red test |
| T3 | `web/e2e/privacy-data.e2e.ts:17-26` + `web/e2e/run.mjs:50, 192, 238` | `E2E_CHAT_PROVIDER` picks the expected recipient regex. The doc comment says "embeddings stay on Google whatever the provider is" | Needs the embeddings provider handed through the same way, so it asserts what the host was told |
| T4 | `web/src/pages/PrivacyDataPage.test.tsx:37, 57, 124, 134` | Fixture strings the test supplies itself (`"Google (Gemini), as our AI model provider"`, "numeric representations") | These prove rendering, not content. Harmless if left |

No test asserts R5's wording. It is unguarded, which is why it survived EXP-21 as a literal.

### 1d. Runtime text checked and found provider-neutral (no change)

- `Art15Disclosure.cs:42-43`: data category "search embeddings of your career narrative". No provider.
- `Art15Disclosure.cs:129-132` (`Art22Logic` step 1): "turned into a numeric representation". No provider.
- `TransparencyNotice.cs:70-149` (`V20260901`): names no provider and does not mention embeddings.
- `PersonalDataDeclaration.cs:24, 84-85`: the `ExpertSearchChunk.Embedding` cascade reason. No provider.
- `ErasureService.cs:166`, `ProcessingRecordService.cs`, `RetentionPolicy.cs`: no provider. A
  `ProcessingRecord` stores the notice version, never a recipient.

---

## 2. Prose sites (`manuals/`, `README.md`, `CLAUDE.md`)

| # | File : line | What it says now | Azure embeddings | Gemini embeddings |
| --- | --- | --- | --- | --- |
| P1 | `manuals/dpia-expert-workspace.md:68-71` | Flow §2: "sent to **Google's Gemini** embedding model… Embeddings go to Google whatever the chat provider is" | "sent to **the configured embeddings provider**" plus the provider list, the way flow §3 (`:72-76`) already handles chat | Same generic wording. Stays true |
| P2 | `dpia-expert-workspace.md:93-110` | Recipients table keyed on `Ai:Chat:Provider` alone: "embeddings stay on Google whatever chat does" | Key the table on (chat, embeddings). Azure/Azure → one row, **Microsoft (Azure OpenAI)**, embedding + scoring. The `:107-110` rule "an edit here that does not also move the code… disagree" still binds: edit it together with R1 | Existing rows stand |
| P3 | `dpia-expert-workspace.md:123-135` | Transfers table: Google row says "embeddings on every deployment" | Google row scoped to "where `Ai:Chat:Provider` or the embeddings provider is Gemini". Microsoft row gains the career narrative (embeddings). Map constraint 4: embeddings run `GlobalStandard`, **not EU-confined**, and the text must say so | Unchanged |
| P4 | `dpia-expert-workspace.md:235` (R7) | "one on a Gemini deployment, two on an Azure one… (Google for embeddings, Microsoft for scoring)… *Google:* unassessed" | On an all-Azure deployment: **one** recipient, Microsoft, and the Google half closed for that deployment (map constraint 3). Residual stays: Microsoft entity/mechanism unassessed, `GlobalStandard` gives no residency | Unchanged |
| P5 | `manuals/gdpr-obligations.md:221-223` | "our embeddings come from a third-party model (`gemini-embedding-001`): the CV text is *disclosed to a recipient*" | "…from the configured embeddings model (`text-embedding-3-small` on Azure, or `gemini-embedding-001`)…" | Stays true |
| P6 | `gdpr-obligations.md:316-320` | "embeddings are Gemini's, while scoring goes to whichever provider `Ai:Chat:Provider` names" | Both configurable, possibly the same party | Stays true |
| P7 | `gdpr-obligations.md:47` | Store table: `EmployeeSearchChunk.Embedding` (`vector(1536)`, `gemini-embedding-001`) | Model id becomes configurable (the entity name is also stale: `ExpertSearchChunk`) | Stays true |
| P8 | `manuals/transparency-and-export.md:50-57` | "three or four categories depending on `Ai:Chat:Provider`… embeddings stay on Google whatever chat does" | Count depends on both axes. Azure/Azure → three categories, Microsoft named once | Stays true |
| P9 | `manuals/expert-workspace-compliance.md:120-125` | Same restatement: "embeddings stay on Google whatever chat does" | As P8 | Stays true |
| P10 | `manuals/expert-privacy-page.md:48` | "Who sees it" row lists the Gemini single entry or the Azure two-way split with Google for embeddings | Add the all-Azure single Microsoft entry. Row `:45` ("that embeddings of their text exist") is neutral | Stays true |
| P11 | `manuals/legitimate-interest-assessment.md:125` | Expectation list: "career narrative was sent to **Google's Gemini** models to be embedded" | "…sent to **Microsoft's Azure OpenAI** service to be embedded". The expectation argument is otherwise unchanged: still a named third party | Stays true |
| P12 | `legitimate-interest-assessment.md:127` | "The second is disclosed to the person **in the notice** and on the access view" | **Already inaccurate, whatever the provider.** The notice names no provider (`TransparencyNotice.cs:92-95`, the gap at `transparency-and-export.md:63-67`). Should say "on the access view" only | Same fix |
| P13 | `manuals/adr-chat-provider-seam.md:17, 67, 76-77, 143-146, 244-246` | "Embeddings are **not** part of this: they keep calling Google whatever chat does"; "A top-level `Ai:Provider` would be a lie, because embeddings still call Google"; §10 lists embeddings as its own effort | An ADR is a decision record, so don't rewrite it. Add a *superseded in part by* pointer to the embeddings ADR. The `Ai:Gemini` key-shape rationale (`:67-77`) is what the new ADR revisits | Unchanged |
| P14 | `README.md:71` | "embeddings always Gemini (`gemini-embedding-001`, 1536 dims), whichever provider chat uses" | Embeddings provider selected by configuration, Azure default | — |
| P15 | `README.md:158-159` | "Embeddings bind `Ai:Gemini` whatever chat does…" | Replaced by the new key shape | — |
| P16 | `README.md:292` | Privacy page names "Google for embeddings and Microsoft for chat, because embeddings never move" | Names the configured providers, one or two | — |
| P17 | `CLAUDE.md:81-83` | "Gemini still serves embeddings (semantic search in the MCP host reads `GEMINI_API_KEY`)" | Operational, not a compliance claim, but it is the agent-facing statement of the same fact | — |
| P18 | `manuals/semantic-roster-search.md:62-64, 155, 427, 451, 498` | Architecture diagram `GeminiEmbedder` / `gemini-embedding-001`, config block, key source, per-model threshold | **Technical, not a compliance claim**: no recipient or transfer statement (only hit for "leaves the service" is `:287`, the exemplar scrub). Update with the seam, not as compliance work | — |

**Historical, no change:** `manuals/provider-naming-compliance-audit.md` (a dated EXP-7 snapshot; its
line numbers are already stale), `manuals/retrieval-eval-baseline.md:105-107` (a dated re-baseline),
and `manuals/wayfinder-map-azure-chat-provider.md`. **No hits:** `retention.md`,
`gdpr-processing-basis.md`, `art22-safeguards.md`, `personal-data-and-erasure.md` (its embedding
lines are about the erasure cascade, provider-neutral), `CONTEXT.md`.

---

## 3. Consent impact: does anything bump `TransparencyNotice.CurrentVersion`?

**No.** This is how the versioning works:

- `CurrentVersion` is a dated string constant (`TransparencyNotice.cs:32`, `"2026-09-01"`). Every
  published version is kept forever in `ByVersion` (`:34-38`), and superseded text is never edited
  (`:18-20`). Changing the words means appending a new version and moving the constant.
- `PendingFor(role, acknowledgedVersion)` (`:65-68`) returns the new version for any account with
  `UserRole.User` whose `AcknowledgedNoticeVersion` differs. Administrators are never prompted.
- It **notifies and does not gate.** "No data is gated on it, nothing is re-collected, and no surface
  is frozen pending a click" (`:56-58`). `NoticeController.cs:66-70`: "Nothing is gated on this —
  a changed notice notifies, it does not withhold anybody's data pending a click". The SPA shows it
  as `web/src/components/NoticeUpdateBanner.tsx`. Acknowledging it records the version against the
  basis already held. It is **not** consent and not a change of lawful basis (`:12-16`: the basis
  is Art. 6(1)(b)/(f)).
- The notice text (`V20260901`, `:70-149`) **names no provider and does not mention embeddings.**
  "Who sees it" (`:92-95`) lists Service Managers and clients only. So moving embeddings from Google
  to Microsoft makes nothing in it false, and there is nothing to re-version.

So the embeddings seam needs no notice bump. "Forcing every account to re-acknowledge" overstates what
a bump does anyway: it is a one-time banner for Users.

**The known gap is unchanged.** The notice's omission of the model provider(s) is the Art. 13(1)(e)
gap recorded at `manuals/transparency-and-export.md:63-67`. Closing it *would* need a new version.
If the build ever does close it, the notice must not hard-name a provider (the notice is static,
while the recipients are per deployment). It should either describe the category ("the AI model
provider(s) this deployment uses, named on your privacy page") or be rendered per provider, which
would mean one notice version per configuration. That choice belongs in the ADR, not in the
embeddings build.

---

## 4. Open questions for the ADR (cannot be settled from the repo)

1. **Past disclosure on a switched deployment.** Art. 15(1)(c) GDPR: "the recipients or categories of
   recipient to whom the personal data **have been or will be** disclosed". After a Gemini → Azure
   switch, every narrative embedded before the switch *has been* disclosed to Google. The repo
   records no switch date and no per-chunk provider (`ExpertSearchChunk` has no provider column), so
   the disclosure cannot currently tell "never sent to Google" from "sent before the switch".
   Options: (a) keep a "Google (Gemini), formerly our embeddings provider" entry on deployments that
   ever ran Gemini embeddings, driven by config; (b) record the provider per chunk or per deployment
   and derive it; (c) accept the understatement and record it in the DPIA. Which is proportionate is a
   legal call this audit cannot make.
2. **Where the Web host learns the embeddings provider** (R7/R8). The chat key has already drifted
   between hosts. A shared key set once by the AppHost, or a cross-host agreement test, would stop a
   repeat. Which one is an ADR decision.
3. **Whether Google keeps the submitted content.** The map quotes Google's unpaid-service terms (use
   for product improvement, human review) with an EEA exception. Whether content already sent is
   retained or deleted cannot be determined from this repo. That is a contract/terms question, and
   it bears on whether "Google closed" in R7 means closed for the future only.

---

## 5. What a build ticket needs (summary)

- **Must change together (one slice):** R1, R4 (new `Microsoft…ForEverything` /
  `…ForEmbeddings` entries), R5, R6, R7, R9, and tests T1, T2, T3. The rule from EXP-7 applies: a
  config migration that leaves the recipient list keyed on chat alone ships a service that names
  Google to people whose narrative never went there. Worse, it would name no embeddings recipient
  on an Azure/Gemini-chat mix.
- **Fix separately, now:** R8, the Web/Agents `Ai:Chat:Provider` drift. It is live on `main` today
  and not caused by this effort.
- **Prose slice (after the seam, one ticket):** P1–P11, P13–P17. P12 is a standalone accuracy fix.
- **No change:** `TransparencyNotice` (no version bump), `PersonalDataDeclaration`, processing
  records, retention, the export DTO, `PrivacyDataPage.tsx`.
