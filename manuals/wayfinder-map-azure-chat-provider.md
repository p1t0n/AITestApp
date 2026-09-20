# Wayfinder map export — a second chat provider (Azure AI Foundry), selected by configuration

Export of Linear map **P1T-241** and its nine child tickets, captured 2026-09-20 for migration to
another Linear workspace. Everything needed to recreate the backlog is here: ticket bodies,
resolutions in full, blocking edges, labels and states. The resolutions are the irreplaceable part —
they are the decisions, and nothing else in the repo records them yet (the ADR is still unwritten,
and is itself the last ticket on this map).

Source workspace: team `P1t0ns nest` (`P1T`), project `AI Test Manager`.
Original map: https://linear.app/p1t0ns-nest/issue/P1T-241

## How to recreate it

1. Create five labels: `wayfinder:map`, `wayfinder:research`, `wayfinder:grilling`,
   `wayfinder:prototype`, `wayfinder:task`.
2. Create the map issue (§1) with `wayfinder:map`.
3. Create the nine tickets (§2) as **child issues** of the map, each with its label and state.
4. Wire the blocking edges (§3) in a second pass — issues need ids before they can reference
   each other.
5. Re-attach the three research notes (§4). They are committed branches in this repo, not Linear
   content, so they survive the move on their own.

---

## 1. The map (P1T-241)

**Label:** `wayfinder:map` · **State:** Backlog

### Destination

An **ADR in `manuals/` plus Ralph-ready build tickets** for a chat-provider seam in `api/Agents`,
where the configuration selects Gemini (today's default) or an Azure OpenAI deployment hosted in
Azure AI Foundry — the spec written against a **real EU Foundry deployment**, not against docs, and
proved by one live tool-calling round-trip.

The map is done when someone can build the seam without asking a question. The build itself is not
on this map.

### Notes

**Domain.** `api/Agents` is the only host that talks to a chat model. Chat wiring today:
`api/Agents/Configuration/GeminiServiceCollectionExtensions.cs` — an `OpenAIClient` against Gemini's
OpenAI-compatible endpoint, registered as one shared `IChatClient` plus a keyed client per agent that
overrides its model under `Gemini:Agents:<agent>`. Two Gemini-specific quirks ride in that pipeline:
`GeminiCompatHandler` (response shim) and `GeminiThoughtSignaturePolicy` (echoes thought signatures
or Gemini 400s). `api/Agents/Program.cs:31` hard-fails in Production without a Gemini key. Eight
agents, all of which need function calling plus JSON output.

**Skills every session consults.** `/grilling` and `/domain-modeling` by default; `/research` for the
AFK tickets; `/prototype` for the live round-trip.

**Standing preferences for this effort.**

- No Azure write of any kind without explicit confirmation of a concrete plan, cost profile listed
  first. Hard ceiling for this map: **$5 total**.
- Package versions live once in `Directory.Packages.props`; a `PackageReference` never carries
  `Version=`.
- Enums persist by name — a new stored provider value is a data concern, not a rename.
- Build tickets that come out of this map are written `ready-for-agent`, so the spec must carry
  literal config keys and literal test names.

**Settled while charting** (constraints, not decisions on the route):

1. Destination is a spec + ADR, not landed code.
2. Chat only. Embeddings out of scope.
3. Either/or selection — one provider active per deployment — shaped so per-agent routing falls out
   free. No failover.
4. No Azure resources exist yet; provisioning is a blocking task on this map.
5. API key auth first, options shaped so an Entra credential can replace it later.
6. Azure OpenAI deployments only, not the Foundry model catalog.
7. `Gemini:*` keys migrate to an `Ai:*` shape with a provider discriminator.
8. Gemini's quirks must not reach the Foundry client — **superseded by the seam ticket**: excluded
   structurally by a construction branch, not by a second factory.
9. Evidence: one live Foundry smoke test. The `Category=eval` gate stays Gemini-only.
10. EU region, subject to model availability — **with the caveat** that at quota Tier 0 only
    `GlobalStandard` exists, which does not confine inference to the EU.
11. Usage rows record provider identity; budget caps stay as they are.

### Not yet specified (fog — in scope, not yet sharp enough to ticket)

- **Failover between providers** when the Gemini free tier exhausts (the repo pins `flash-lite`
  precisely because RPD is 500 vs 20) — wants budget, retry and breaker semantics that nothing here
  has decided.
- **Per-agent provider routing** — the seam makes it mechanically free, but which agent belongs on
  which provider is a quality question nobody has evidence for yet.
- **Re-running the `Category=eval` tool-selection gate per provider**, if Foundry ever becomes
  primary rather than an alternative.
- **Provider-aware budget caps** — a token cap tuned to a free tier means something different when
  tokens cost money.
- **Entra ID / `DefaultAzureCredential`** replacing the API key, including the local-dev and CI
  credential story.
- **How the Aspire AppHost represents an Azure provider locally** — a parameter like the Gemini key,
  or something that knows about the Azure resource.
- **Whether the agent layer eventually moves to the Responses API.** Every `gpt-5.6-*` model refuses
  a Chat Completions request carrying `tools`, so the newest and longest-lived Foundry models are
  unreachable through `AsIChatClient()` as this repo uses it. Not a problem for `gpt-4.1-mini` (alive
  to 2027-04-14), and squarely a Gemini question too — but it is the thing that will force the next
  provider decision.
- **Whether to close the Art. 13(1)(e) transparency gap in the same pass** —
  `TransparencyNotice.cs:92-95` names no provider at all (a gap that predates this effort), and
  closing it needs a `CurrentVersion` bump re-acknowledged by every account. Cheaper once than twice,
  but it drags a re-consent flow into a plumbing change.
- **Whether `CONTEXT.md` needs a term** for the provider seam. (The
  `manuals/cloudflare-workers-ai-provider.md` half of this is answered enough to hand to the ADR
  ticket — see §5.)

### Out of scope (ruled beyond the destination; never graduates)

- **A second embeddings provider.** The pgvector column fixes 1536 dimensions and the roster is
  already embedded; a second embedding backend means dimension-matching or a full re-embed
  migration. Different blast radius, its own effort.
- **Foundry model catalog models** (Llama, Mistral, DeepSeek, Grok, served serverless). Different SDK
  stack and a different function-calling story per model; the seam here targets Azure OpenAI
  deployments only.
- **Failover/fallback as a shipped behaviour** — ruled out of *this* destination, which is either/or
  selection. (The motivation survives as fog.)

---

## 2. The tickets

### 2.1 P1T-242 — Which EU-region Azure OpenAI deployment can serve all eight agents, and what does it cost?

**Label:** `wayfinder:research` · **State:** Done

**Question (abridged — the resolution supersedes it).** Which Azure OpenAI chat models are
deployable in an EU region and support tool calling plus JSON-schema output; EU per-1M-token pricing
for the cheapest qualifying rows; deployment types and which keep data in-region or carry an idle
charge; default new-subscription quota; and whether a Foundry *project* is required at all.

**Resolution.**

> **Sweden Central · `gpt-4.1-mini` (2025-04-14) · `DataZoneStandard`** — ~**$1.76 per 1,000 test
> calls** at 2k in / 500 out, ~$2.92 at this repo's own measured roster-scan call size (5,112
> tokens/call, `manuals/agent-cost-budgets.md` §1.2). Falls back to `GlobalStandard` (~$1.60 /
> ~$2.66) if the subscription is still at quota Tier 0.
>
> **The four facts that decide it**
>
> 1. **Region is free; the deployment type is what costs.** Global Standard is byte-identical in
>    Sweden Central, West Europe, France Central, Switzerland North *and* East US. The EU premium
>    belongs to the deployment type: Data Zone +10%, EU regional Standard ×1.21 of Global — and
>    **West Europe regional is ×1.32**, ~9% dearer than Sweden or France for nothing. Verified
>    meter-by-meter against the public Azure Retail Prices API (`serviceName eq 'Foundry Models'`;
>    `'Cognitive Services'` returns zero rows, which is why that filter looks empty).
> 2. **Quota Tier 0 is three chat models wide, `GlobalStandard` only** — `gpt-4.1-mini` (200 RPM /
>    200k TPM), `gpt-5-mini`, `o4-mini`, plus `text-embedding-3-small`. **No Data Zone row, no
>    regional row.** The bottom tier therefore offers only the deployment type that does *not* keep
>    inference in the EU; Data Zone quota starts at Tier 1. No quota *request* is needed for a first
>    deployment, and asking pre-emptively is likely to be denied. Tier is checkable read-only:
>    `GET .../providers/Microsoft.CognitiveServices/quotaTiers?api-version=2025-10-01-preview`.
> 3. **Every `gpt-5.6-*` model hard-fails a Chat Completions request carrying `tools`** — *"Function
>    tools with reasoning_effort are not supported … use /v1/responses or set reasoning_effort to
>    'none'"* — and it fires without sending `reasoning_effort` at all, because the default is
>    `medium`. `AsIChatClient()` speaks Chat Completions, so the newest, longest-lived Foundry models
>    are exactly the ones this repo cannot adopt without moving the agent layer to the Responses API.
> 4. **Retirement clock.** The whole o-series (`o4-mini`, `o3-mini`, `o3`, `o1`) retires
>    **2026-11-19**. `gpt-4o` 2024-05-13 retires **2026-10-01** and is also the one model that
>    explicitly does not support `json_schema`. `gpt-4.1-mini` is Legacy but alive to **2027-04-14**;
>    `gpt-5-mini` is GA to 2027-02-09.
>
> **What contradicts the question as written**
>
> - **Two doc pages named in the ticket are 404.** The product is now *Microsoft Foundry*;
>   `/azure/ai-foundry/...` redirects to `/azure/foundry/...`, and `concepts/model-matrix` and
>   `concepts/data-residency` are deleted. The function-calling page **no longer lists models or
>   api-versions at all**.
> - **The deployment-type list was incomplete and one name drifted.** Current SKUs: `GlobalStandard`,
>   `DataZoneStandard`, `Standard`, `GlobalBatch`, `DataZoneBatch`, `GlobalProvisionedManaged`,
>   `DataZoneProvisionedManaged`, `ProvisionedManaged` (now "Regional Provisioned"), plus
>   `DeveloperTier` (fine-tuned only, 24-hour lifetime, no residency guarantee). Batch is 50% off.
> - **"Idle/standing charge" exists only on Provisioned.** No Standard/Global/DataZone/Batch
>   deployment of a base model carries an hourly fee, and the resource itself bills nothing. The
>   `$1.70/hour` figure in the pricing data is fine-tuned-model hosting. The real hazard is the
>   inverse: a `*ProvisionedManaged` meter *"starts when the deployment is created and stops when
>   it's deleted"* regardless of tokens — that alone blows $5 in well under an hour.
> - **Cheapest ≠ available.** `gpt-5-nano` and `gpt-4.1-nano` are far cheaper but appear in no Tier 0
>   quota table. `gpt-5-mini` / `gpt-4o-mini` have **no regional-Standard meter in any region** —
>   `gpt-4.1-mini` is the only cheap chat model with that escape hatch.
> - **"EU region" and "EU data zone" are not the same set.** Switzerland North has no Data Zone meter
>   for any cheap chat model, despite the deployment-types page saying the EU zone *"can include EFTA
>   countries"*. Norway East likewise.
> - **No Foundry project is required.** A plain `kind: OpenAI` resource is not deprecated, and
>   `https://<name>.openai.azure.com/openai/v1/` takes an `ApiKeyCredential` with `model` carrying the
>   **deployment name** — the same `OpenAIClient` shape
>   `GeminiServiceCollectionExtensions.cs:32-41` already builds, minus the two shims. Key auth covers
>   inference; Entra is mandatory only for Agents/Evaluations/Toolbox, which this repo does not use.
>
> **Two traps for the build ticket.** 429s *below* your quota are documented and expected on shared
> Standard pools — compare `x-ratelimit-limit-tokens` in the response header; do not "fix" it by
> requesting quota. And rate-limit accounting charges `max_tokens`, not actual output, while this
> repo's agents set generous ceilings.
>
> **Unresolved:** `gpt-4o-mini`'s structured-output support is contradictory across two live pages.
> Does not affect the recommendation.

### 2.2 P1T-243 — Which compliance artifacts hard-name the model provider?

**Label:** `wayfinder:research` · **State:** Done

**Question (abridged).** A second inference provider is a new sub-processor for personal data. Which
parts of this repo's compliance machinery name Google/Gemini as *the* provider and would be wrong
once the provider is configurable — the compliance manuals, the SPA's transparency surface, any API
response naming the provider to an expert, the build-enforced `PersonalDataDeclaration`, and any
written transfer/residency claim?

**Resolution.**

> **Four must-change sites, and they are all the same fact told four times: the Art. 15(1)(c)
> recipient category.** One of them is code that a data subject actually reads; the rest of the
> provider naming is prose. 22 sites audited, 16 name Google/Gemini: 4 must change, 10 should, 8 no
> change.
>
> **Must change**
> 1. `api/Application/Compliance/Art15Disclosure.cs:62-65`
> 2. `api/Application/Compliance/AccessAndExportService.cs:260-264`
> 3. `tests/Web.Tests/TransparencyTests.cs:51-59`
> 4. `web/e2e/privacy-data.e2e.ts:38`
>
> **The load-bearing one** is #1 — `"Google (Gemini), as our AI model provider"` is not
> documentation. It is served verbatim by `GET /api/me/access` and rendered on the expert's privacy
> page; `PrivacyDataPage.tsx:266-272` is fully server-driven, so that string is the *only* source of
> the provider name the expert sees. Ship a different provider with that literal in place and the
> service names the wrong recipient to a data subject — and that naming is exactly the mitigation the
> DPIA cites for risk R7.
>
> **Verdict.** The recipient category must become provider-derived **inside the seam's build
> tickets**. Tests #3/#4 assert the *configured* provider while keeping the `outside this company`
> floor. The ten prose edits are a separate, smaller ticket, blocked by the seam and best written
> once the real Foundry resource identity exists; they do not block the seam.
>
> **Two findings that change assumptions**
> - **The build-enforced machinery needs nothing.** `PersonalDataDeclaration.cs` names no provider; it
>   keys on stores carrying an `ExpertId`/`UserId`. A provider column on `AgentUsage` (already
>   declared at `:122-126`) keeps `PersonalDataDeclarationTests` green. Only a genuinely new
>   person-keyed table would trip it.
> - **An EU region breaks no written claim, because there is no written claim.** A sweep for
>   residency/transfer language returns three hits, all in the DPIA, all saying the question is
>   *open*: `dpia-expert-workspace.md:68,71`, `:91-95` ("Not assessed here, and it needs to be"),
>   `:197` (R7, "Open"). EU region is upside, but a supplementary measure, not a transfer mechanism —
>   it does not close R7. Honest edit: a per-provider row in DPIA §1, R7 downgraded to "assessed for
>   one provider, open for the other".
>
> **Adjacent, not created by this effort:** `TransparencyNotice.cs:92-95` names no provider at all — a
> known Art. 13(1)(e) gap recorded in `transparency-and-export.md:53-58`, whose closure needs a
> `CurrentVersion` bump re-acknowledged by every account. Also: no expert-facing response names a
> *model* anywhere.

### 2.3 P1T-244 — How does AzureOpenAIClient reach IChatClient, and where does it differ from the Gemini path?

**Label:** `wayfinder:research` · **State:** Done

**Question (abridged).** Package and version for `AzureOpenAIClient` and its constraint on the
`OpenAI` package; the API-key ctor shape and endpoint URL; deployment-name vs model-id addressing;
`.AsIChatClient()` availability and the required `Microsoft.Extensions.AI` version; whether any
analog of the two Gemini hacks is needed; structured-output support; and whether the ServiceDefaults
resilience handler could attach.

**Resolution.**

> **No Azure-side quirk handling is needed, `Microsoft.Extensions.AI` needs no bump, and the seam is
> cheaper than the map assumed — the provider switch is one factory over one singleton, with the keyed
> per-agent clients, `Instrument()` and `ResolveAgentChatClient` untouched.**
>
> Evidence is live docs *plus two compile-and-run probes* against the real packages on net10.0 — the
> version and type facts come from the probes, not doc prose.
>
> **Route B recommended: plain `OpenAI` against the v1 endpoint**
> - **Zero new package versions.** `OpenAI` isn't in `Directory.Packages.props` and no csproj names
>   it; it arrives transitively via `Microsoft.Extensions.AI.OpenAI`. Making the reference honest is
>   one entry: `OpenAI 2.12.0`, which must stay in `[2.12.0, 2.13.0)` — the range M.E.AI **10.9.0**
>   pins.
> - **Route A** (`Azure.AI.OpenAI`) costs one entry at **2.9.0-beta.1** — a prerelease, and the newest
>   thing that exists: last stable is 2.1.0 from Dec 2024.
> - Microsoft's own .NET docs now show Route B, the 2.9.0-beta.1 release notes *recommend removing*
>   `Azure.AI.OpenAI` in favour of the OpenAI SDK, and the Foundry .NET language-support page
>   documents only `OpenAI` (tested at 2.12.0 — the exact version this repo resolves) plus
>   `Azure.Identity`.
>
> **No quirk analogs, on either route**
> - No `GeminiThoughtSignaturePolicy` analog: thought-signature echo is a Gemini-3 rule, and the
>   nearest relative (encrypted reasoning items) is a **Responses API** feature. **Re-open this if the
>   seam ever moves to `ResponsesClient`.**
> - No `GeminiCompatHandler` analog: Azure returns OpenAI's own `finish_reason` values plus
>   `content_filter`, already in the SDK enum *and* in this repo's `KnownFinishReasons`.
> - **The one genuinely new behaviour is content filtering**: a filtered *prompt* is an HTTP **400**
>   with `code: "content_filter"`; a filtered *completion* is a 200 with
>   `finish_reason: "content_filter"` and possibly no content.
>
> **What the seam looks like.** `AzureOpenAIClient : OpenAIClient`, and `AsIChatClient()` is a single
> extension on `OpenAI.Chat.ChatClient` with no Azure overload — all three probed constructions return
> the identical `OpenAIChatClient`. Today's `AddSingleton(_ => new OpenAIClient(...))` +
> `GetRequiredService<OpenAIClient>().GetChatClient(model)` would accept an `AzureOpenAIClient` **with
> no change to the resolution code**.
>
> **Three provider-shaped differences that each want an ADR line**
> 1. **`Model` means "deployment name" on Azure** — an operator-chosen string, meaningless outside its
>    resource, unlike a catalogue id pinned for quota.
> 2. **The ServiceDefaults resilience handler becomes attachable — and is a foot-gun.**
>    `AddStandardResilienceHandler` defaults to a **10 s per-attempt timeout, 30 s total, 3 retries**;
>    the attempt timeout would cancel healthy tool-calling completions and retries stack on the SDK's
>    own.
> 3. **Structured output becomes GA and documented per model** (`json_schema` + `strict: true`, with
>    `parallel_tool_calls` false alongside).
>
> **Left open, by design:** which hostname a real EU Foundry resource prints
> (`*.openai.azure.com` vs `*.services.ai.azure.com`), and any wire behaviour at all, since no probe
> called a service.

### 2.4 P1T-245 — Provision the Foundry resource and one chat deployment, under $5

**Label:** `wayfinder:task` · **State:** In Progress (claimed, **blocked on Azure credentials**)

**This is the only ticket on the map that writes anything.**

**Gate before any write.** Present a concrete plan and get an explicit yes first: exact resource
types, exact names, exact region, the deployment SKU and capacity, and a cost profile — standing cost
per day if idle, and cost per 1000 test calls at the chosen model's rate. No resource is created
before that yes.

**Acceptance criteria**

- Hard ceiling **$5 total** for the lifetime of this map. If the plan cannot show a path to staying
  under it, stop and report instead of provisioning.
- Region and model as recommended by P1T-242, unless quota or availability forces a documented
  deviation.
- One resource group holding everything, so teardown is one command. Write the teardown command into
  the resolution.
- A deployment that actually answers: prove it with one `curl`/CLI chat completion and paste the
  response.

**Record in the resolution** — later tickets depend on these as facts: subscription id, resource
group, resource name, region; the endpoint URL exactly as the seam needs it; the deployment name (not
the model id) and the model version behind it; where the API key lives (`dotnet user-secrets` path
and env var name — **never the key itself**); observed quota (TPM/RPM) and whether an increase was
needed; actual spend, and the standing daily cost if the deployment is left in place.

**The plan, from P1T-242**

- **Sweden Central · `gpt-4.1-mini` (2025-04-14) · `DataZoneStandard`**, on a plain `kind: OpenAI`
  resource — **no Foundry project required**. ~$1.76 per 1,000 calls; ~$2.92 at this repo's measured
  call size.
- **Check the quota tier before anything else**, read-only:
  `GET .../providers/Microsoft.CognitiveServices/quotaTiers?api-version=2025-10-01-preview`.
  **Tier 0 carries `GlobalStandard` only — no Data Zone row.** If the subscription is Tier 0, the
  EU-inference property this map wanted is not available at the bottom tier, and **that is a decision
  to put to the human before provisioning, not a deviation to absorb**: take `GlobalStandard` now and
  record that inference is not EU-confined, or wait for Tier 1. Do not request a quota increase
  pre-emptively.
- **Never create a `*ProvisionedManaged` deployment.** Its meter runs from creation to deletion
  regardless of tokens — that alone blows the ceiling in under an hour.
- **Not West Europe** (×1.32 of Global for an identical service). **Not Switzerland North or Norway
  East** (no Data Zone meter for any cheap chat model).
- **Not any `gpt-5.6-*` model**; **not the o-series**; **not `gpt-4o` 2024-05-13**.
- Expect **429s below your stated quota** on shared Standard pools — compare
  `x-ratelimit-limit-tokens`; not a quota problem.

Endpoint to record: `https://<name>.openai.azure.com/openai/v1/`, with `model` carrying the
**deployment name**.

**Current blocker (2026-09-20).** Azure credentials are expired — `AADSTS50173: The provided grant
has expired due to it being revoked`. The CLI token was issued 2026-07-13; `TokensValidFrom` for the
account is 2026-09-02. Every credential in the chain fails, so even read-only recon is impossible
until an interactive `az login`. Nothing was created; nothing was read.

### 2.5 P1T-246 — What shape is the chat-provider seam?

**Label:** `wayfinder:grilling` · **State:** Done

**Resolution.**

> **One entry point, `AddChatProvider(config)`, switching on `Ai:Chat:Provider` and sharing
> everything after client construction. The provider-specific part is a single constructor; the
> isolation the map asked for is structural, because the Gemini shims are attached inside a branch
> that is unreachable when the provider is Azure.**
>
> **Two facts found in the code that moved the answers**
> - **`MeteringChatClient` reports `response.ModelId`** (`api/Agents/Usage/MeteringChatClient.cs:155`),
>   not a config value. Azure echoes the real model in the response
>   (`gpt-4.1-mini-2025-04-14`), so the deployment-name-vs-model-id concern raised by P1T-244 does
>   **not** reach usage metering.
> - **`EmbeddingOptions.Section` is `"Gemini"`**
>   (`api/Infrastructure/Embeddings/EmbeddingOptions.cs:14`) — chat and embeddings bind the *same*
>   config block, sharing endpoint and key.
>
> **Decisions**
> 1. **Route B** — plain `OpenAI` against `https://<res>.openai.azure.com/openai/v1/`.
> 2. **One `AddChatProvider(config)`**, switching internally. The downstream wiring — `Instrument`,
>    the keyed per-agent loop, `ResolveAgentChatClient`, the budget wrapper — is already
>    provider-neutral and changes not at all. **This supersedes the map's constraint 8 wording**: the
>    shims are excluded *structurally*, which is the property that mattered, not the count of
>    factories.
> 3. **The discriminator is chat-only: `Ai:Chat:Provider`.** A top-level `Ai:Provider` would be a lie
>    — it would read as "this app uses Azure" while embeddings still call Google. `Ai:Gemini:*` holds
>    the endpoint and key that chat *and* embeddings bind.
> 4. **Fail at startup, everywhere, on an unknown provider value**; keep missing-credential failure
>    Production-only, as today.
> 5. **Do not attach the ServiceDefaults resilience handler**, on either provider. Its 10 s
>    per-attempt timeout cancels healthy tool-calling completions and its retries stack on the SDK's
>    own.
> 6. **Normalize content filtering into one typed failure** in the shared decorator stack, alongside
>    Gemini's own safety-block shapes. Orchestration degrades a failed stage rather than failing the
>    call — two provider-specific shapes leaking into it would put provider knowledge in the one layer
>    that must have none.
> 7. **Strict structured output stays off on both.**
> 8. **Per-agent overrides live inside the provider's own block** — `Ai:Gemini:Agents:<agent>` and
>    `Ai:AzureFoundry:Agents:<agent>`.
>
> **Tests the build tickets must name literally**
> - `ChatProviderRegistrationTests.UnknownProvider_ThrowsAtStartup`
> - `ChatProviderRegistrationTests.GeminiProvider_AttachesCompatHandlerAndSignaturePolicy`
> - `ChatProviderRegistrationTests.AzureProvider_ConstructsNoGeminiCompatHandler` — asserted
>   **structurally**; the shim being *absent* is the safety property, and an absence regresses
>   silently.
> - `ChatProviderRegistrationTests.PerAgentOverride_ResolvesKeyedClient_ForBothProviders`
> - `ChatProviderRegistrationTests.EveryClient_IsWrappedInMeteringAndBudget`
> - a config-migration test (see P1T-247)
>
> **Vocabulary, for the ADR.** **Provider** — the backend a chat call goes to (`Gemini` |
> `AzureFoundry`), named by `Ai:Chat:Provider`. **Seam** — `AddChatProvider`, the one place a provider
> is chosen. **Construction branch** — the provider-specific few lines that build the `OpenAIClient`
> and, for Gemini only, attach its two shims. Everything after the seam is *provider-neutral*.

### 2.6 P1T-247 — How do the Gemini:* keys migrate to an Ai:* shape?

**Label:** `wayfinder:grilling` · **State:** Done

**Resolution.**

> **The rename is ~45 files and ~180 sites, but almost all of them are the env var `GEMINI_API_KEY`,
> which survives untouched. The actual config-key migration is small: two `appsettings.json` blocks,
> three options classes, the AppHost parameter, one `tools/` console and three tests.**
>
> **Inventory**
>
> | Surface | Sites | Migrates? |
> |---|---|---|
> | `api/Agents/appsettings.json:8`, `api/Mcp/appsettings.json:11` | 2 blocks | yes |
> | `api/Agents/Configuration/AgentsOptions.cs`, `GeminiServiceCollectionExtensions.cs` | 9 | yes |
> | `api/Infrastructure/Embeddings/{EmbeddingOptions,EmbeddingServiceCollectionExtensions}.cs` | 7 | yes — `Section` is `"Gemini"` |
> | `api/AppHost/Program.cs` (the `gemini-api-key` parameter) | 6 | yes |
> | `api/Agents/Program.cs` (the Production guard) | 4 | yes |
> | `tools/RetrievalEval/Program.cs` | 7 | yes — the only console spelling `Gemini:` keys |
> | `tests/…` — `RetrievalEvalLiveTests`, `EmbeddingLiveSmokeTests`, `ModelSelectionTests` | ~15 | yes |
> | ~20 other test files, 3 other `tools/` consoles | ~60 | **no** — they read the env var directly |
> | `README.md`, `CLAUDE.md`, `.claude/skills/verify/SKILL.md`, `manuals/*` | ~25 | docs pass |
> | `.github/workflows/ci.yml` | **0** | **CI never sets the key** — not a migration surface |
>
> **Decisions**
> 1. **The literal keys**, to be quoted verbatim by the build tickets:
>    - `Ai:Chat:Provider` — `Gemini` | `AzureFoundry`
>    - `Ai:Gemini:{Endpoint, Model, ApiKey, EmbeddingModel, Dimensions, QuotaBreakerSeconds, Agents:<agent>}`
>    - `Ai:AzureFoundry:{Endpoint, Model, ApiKey, Agents:<agent>}`
>
>    `Ai:Gemini` keeps the embedding keys because chat and embeddings genuinely share that endpoint
>    and key. **`Ai:AzureFoundry:Model` holds a deployment name** — the spelling is shared, the
>    meaning is per-provider, documented in the ADR rather than forking the override dictionary.
> 2. **`GEMINI_API_KEY` survives**, with `AZURE_FOUNDRY_API_KEY` added alongside in the same explicit
>    style. It is a credential name, not a config path — the code reads it explicitly rather than
>    binding it.
> 3. **A legacy `Gemini:` section fails loudly at startup.** No deprecation window: nothing external
>    consumes this config, and a silently-ignored stale key is precisely the failure the seam's
>    startup throw exists to prevent.
> 4. **All four `tools/` consoles are migrated** — which costs exactly one file.
> 5. **`AgentsOptions.cs` splits into two independent classes**, `GeminiOptions` and
>    `AzureFoundryOptions`, no shared base; `EmbeddingOptions.Section` becomes `"Ai:Gemini"`. A base
>    class would imply the two providers must stay shaped alike — the coupling the seam avoided.
> 6. **`ConfigKeyMigrationTests.NoLegacyGeminiConfigKeyRemains`** — reads the repo's own source and
>    fails on any `"Gemini:"` string outside `manuals/` and the ADR. An established pattern here:
>    `web/src/frozenHooks.test.ts` already reads app source and fails on a renamed `data-testid`.
>    `ModelSelectionTests` is rewritten onto the new keys and kept as the canary.
>
> **One ticket amended by this session.** P1T-143's Cloudflare gate was **committed as a skipped
> `Category=live` probe**, not a throwaway script, explicitly because "the measurement needs a key we
> do not have" and a throwaway would be "written, never run, and lost"
> (`tests/Agents.Tests/CloudflareWorkersAiGateTests.cs:25-31`). P1T-249 is rewritten to follow that
> precedent.

### 2.7 P1T-248 — Where does provider identity land on a usage row?

**Label:** `wayfinder:grilling` · **State:** Done

**Resolution.**

> **A nullable `Provider` string column on `AgentUsage`, written as `provider.ToString()`, no
> backfill — and the deletion of a config fallback in `UsageMeter` that would otherwise have been
> migrated into a new set of key names while staying a lie.**
>
> **What the code says**
> - `AgentUsage.Model` is a plain string of the model the *response* named. Nothing branches on it.
> - **Nothing reads it.** `UsageService.GetSnapshotAsync` selects only `Timestamp`, `AgentName`,
>   `TotalTokens` (`api/Agents/Usage/UsageService.cs:55-58`). Write-only diagnostic data.
> - `UsageMeter.cs:32-34` falls back to `config["Gemini:Agents:{agent}"]` then `config["Gemini:Model"]`
>   when `reply.ModelId` is null — **two config sites the migration would otherwise carry**, in code
>   whose own comment says it "mislabels whenever config and reality drift".
>
> **Decisions**
> 1. **Add the column** — `string? Provider` on `AgentUsage`, nullable. The counter-case (model ids
>    already distinguish the providers; nothing reads either field) was put explicitly and rejected:
>    model ids drift across aliases and version suffixes, and deriving provider by parsing a model
>    string fails exactly when cost attribution starts to matter.
> 2. **Enum at the config edge, string in the database.** `ChatProvider` is an enum where
>    `Ai:Chat:Provider` binds, so an unknown value throws at startup; the row stores
>    `provider.ToString()`. This is also why the **enums-persist-by-name** rule does not bite: no enum
>    property reaches the EF model, so `AppDbContext`'s conversion loop never sees one.
> 3. **Delete the config fallback** rather than migrate it. Record `""` when the reply never reached a
>    model; `Iterations = null` already encodes that case.
> 4. **No backfill.** Null means "written before providers existed" — the same convention
>    `LatencyMs`, `Iterations` and `ToolSequence` already use for legacy rows.
> 5. **Tests**: `UsageMeterTests.Records_the_active_provider` and
>    `UsageMeterTests.Records_the_response_model_not_a_config_lookup` (the second pins decision 3 so
>    the fallback cannot creep back).
> 6. **Nothing to do for telemetry.** `UseOpenTelemetry` already emits `gen_ai.system` from the
>    library. The build ticket verifies it rather than implementing it.
>
> **Knock-on.** `PersonalDataDeclaration` stays green without an edit — `AgentUsage` is already
> declared (`PersonalDataDeclaration.cs:122`), and this adds a column to a declared store rather than
> a new person-keyed table. The migration is one `AddColumn`.

### 2.8 P1T-249 — Prove the dialect: one live tool-calling round-trip through the Azure client

**Label:** `wayfinder:prototype` · **State:** Backlog (open)

Docs say the Azure path is the same OpenAI dialect. This ticket finds out, cheaply, before the ADR
commits to it.

**What it must do.** Build the client against the real deployment with an API key, get an
`IChatClient`, and run **one round-trip that calls a tool** — declare a trivial function, let the
model call it, return a result, get the final message. Then a second run with a **multi-turn history
that includes a prior tool call and result**, because that is precisely where the Gemini path needed
`GeminiThoughtSignaturePolicy`.

**What to report**

- Does a replayed tool-call history survive without a policy analog — the one fact the seam's shape
  depends on.
- Does the response deserialize without a `GeminiCompatHandler`-style shim, or is there an Azure-side
  shape quirk of its own.
- Does `ChatResponseFormat.ForJsonSchema` work end to end, and does the model honour strict schema.
- Token counts and the actual cost of the run, against the $5 ceiling.
- Anything that would make one of the eight agents behave differently here than on Gemini.
- **Which hostname the real resource prints** — `*.openai.azure.com` or `*.services.ai.azure.com`.
- **Content filtering**, if a run trips it: record both shapes. Do not go hunting for it with
  deliberately bad prompts.

**Committed, not throwaway.** Follow `tests/Agents.Tests/CloudflareWorkersAiGateTests.cs:25-31`: a
committed probe that **skips without an Azure key**, runs with one command the day a key exists, and
doubles as the live smoke test the map wants as evidence. Run it on whichever route the seam picked
(Route B).

**Since no quirk analog is *known* on the Azure side, this ticket is now the only thing that can
falsify that.**

### 2.9 P1T-250 — Write the ADR and the Ralph-ready build tickets

**Label:** `wayfinder:task` · **State:** Backlog (open) · **The destination.**

**1. `manuals/adr-chat-provider-seam.md`** — the decision record. Provider selected by configuration;
one seam with a per-provider construction branch; Azure OpenAI deployments only; API key now with the
Entra path named as a later move; EU region and what residency claim that supports; what was ruled
out (embeddings, catalog models, failover) and why. Links each decision to its ticket. States what
would make us revisit.

**2. Build tickets**, `ready-for-agent`, sized for the Ralph loop and `blockedBy`-ordered, with
acceptance criteria precise enough to build from without asking — literal config keys, literal test
names, literal file paths. Expect roughly: the seam + Gemini construction-branch refactor (no
behaviour change, the safety net for everything after), the Azure branch, the config migration, the
provider-aware startup guard, the usage-row column + migration, the live probe, the Art. 15 recipient
slice, and the prose-only compliance ticket.

**Acceptance criteria**

- No build ticket contains an open question. If one does, it belongs on the map instead, and the map
  is not finished.
- The ADR's claims about the Azure path cite the probe's observed behaviour, not the docs, wherever
  the two could differ.
- `CONTEXT.md` gets a term only if the seam introduces domain vocabulary — check, decide, say which.
- Say plainly whether `manuals/cloudflare-workers-ai-provider.md` is superseded, still live as an
  alternative, or stale — see §5.

---

## 3. Blocking edges

Wire these after all issues exist. `A ← B` means "A is blocked by B".

```
P1T-245  ←  P1T-242
P1T-246  ←  P1T-244
P1T-247  ←  P1T-246
P1T-248  ←  P1T-246
P1T-249  ←  P1T-244, P1T-245
P1T-250  ←  P1T-243, P1T-246, P1T-247, P1T-248, P1T-249
```

P1T-242, P1T-243 and P1T-244 were unblocked from the start and are now closed. The live frontier is
**P1T-245** (claimed, blocked on Azure credentials); P1T-249 and P1T-250 follow it.

## 4. Research notes (branches, not Linear content)

Pushed to `origin`, one file each, no PR:

| Branch | Note | Ticket |
|---|---|---|
| `research/azure-foundry-model-and-cost` | `manuals/azure-foundry-model-and-cost.md` (469 lines) | P1T-242 |
| `research/provider-naming-compliance-audit` | `manuals/provider-naming-compliance-audit.md` (142 lines) | P1T-243 |
| `research/azure-openai-ichatclient-wiring` | `manuals/azure-openai-ichatclient-wiring.md` (500 lines) | P1T-244 |

These survive the workspace migration on their own; re-attach them as links on the recreated tickets.

## 5. Relationship to the earlier Cloudflare effort (P1T-143)

P1T-143 — *Second chat provider behind the IChatClient seam: Cloudflare Workers AI as a Gemini-429
fallback* — is marked **Done**, but only its gate prototype landed (PR #117, a skipped live probe).
**The gate was never run**, because no Cloudflare key exists. Its "if the gate passes" plan proposed
widening config to **per-agent provider profiles** (`agent → { endpoint, apiKey, model }`), which is a
*different* shape from what this map decided (a global `Ai:Chat:Provider` with per-agent
model/deployment overrides inside the active provider).

The ADR must say explicitly that it supersedes that plan, and that **Cloudflare remains an un-gated
option rather than a rejected one** — its motivation (Gemini's free tier is a single point of failure)
is this map's failover fog, not a dead end.
