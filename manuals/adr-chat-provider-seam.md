# ADR: the chat provider is chosen by configuration, behind one seam

**Status:** accepted, 2026-09-20. Charted as Linear map EXP-5, whose nine decision tickets carry the
evidence behind every claim here; the map itself is exported at
`manuals/wayfinder-map-azure-chat-provider.md`. Supersedes the per-agent provider-profile plan
sketched in `manuals/cloudflare-workers-ai-provider.md` (see §8). No code implements this yet — the
build tickets are listed in §10.

## The decision

`api/Agents` gets **one seam**, `AddChatProvider(config)`, which reads `Ai:Chat:Provider` and builds
either today's Gemini client or an **Azure OpenAI deployment** client. Everything downstream of
client construction — the keyed per-agent loop, `Instrument()`, `ResolveAgentChatClient`, the runtime
budget wrapper, the metering decorator — is already provider-neutral and does not change.

One provider is active per deployment. There is no failover and no per-agent provider routing.
Embeddings are **not** part of this: they keep calling Google whatever chat does.

Authentication is an API key now, with the options shaped so an Entra credential can replace it
later. The Azure side is `gpt-4.1-mini` on a plain `kind: OpenAI` resource in Sweden Central — **no
Foundry project**, and **no EU-residency claim** (§7).

## 1. Vocabulary

Three words, used precisely for the rest of this document and by the build tickets:

**Provider** — the backend a chat call goes to. Exactly two values: `Gemini` | `AzureFoundry`, named
by `Ai:Chat:Provider`.

**Seam** — `AddChatProvider(config)`. The one place a provider is chosen. Nothing else in the repo
may branch on provider identity.

**Construction branch** — the provider-specific few lines inside the seam that build the
`OpenAIClient` and, **for Gemini only**, attach its two shims. Everything after the seam is
*provider-neutral*: it holds an `IChatClient` and cannot tell who is behind it.

`CONTEXT.md` **gets no new term.** All three words above are implementation vocabulary, not domain
language — they describe how a call is wired, not anything the business talks about. The one
provider-adjacent phrase the domain does own is already there and already neutral: **Access View**
(`CONTEXT.md:95-99`) says "who it reaches — including the named model provider" without naming
Google. That entry stays as written. (EXP-14's acceptance criterion: checked, decided, recorded.)

## 2. The decisions

Each links to the ticket that settled it. Where a claim could differ between documentation and
reality, it cites what the live probe **observed** (§3), not what a page said.

**1. Route B — the plain `OpenAI` package against the v1 endpoint.** `EXP-8`
`new OpenAIClient(credential, new OpenAIClientOptions { Endpoint = "https://<res>.openai.azure.com/openai/v1/" }).GetChatClient("<deployment>").AsIChatClient()`.
Not `Azure.AI.OpenAI`, whose newest release is a prerelease (2.9.0-beta.1) whose own notes recommend
removing it in favour of the OpenAI SDK. `AzureOpenAIClient` derives from `OpenAIClient` and all
three probed constructions return the identical `OpenAIChatClient`, so Route A would have bought a
prerelease dependency for no type difference.

*Package cost:* one `PackageVersion` entry, `OpenAI 2.12.0`, making honest a reference that already
arrives transitively via `Microsoft.Extensions.AI.OpenAI`. It must stay inside `[2.12.0, 2.13.0)`,
the range M.E.AI 10.9.0 pins. **`Microsoft.Extensions.AI` needs no bump.**

**2. One seam with a construction branch, not two factories.** `EXP-10`
The map originally asked that Gemini's quirks "must not reach the Foundry client" and assumed that
meant a second factory. It does not: the shims are attached *inside a branch that is unreachable when
the provider is Azure*, which is structural exclusion — the property that actually mattered. A second
factory would have duplicated the keyed-per-agent registration, which is the one piece both providers
need identically.

**3. The discriminator is chat-only: `Ai:Chat:Provider`.** `EXP-10`
A top-level `Ai:Provider` would be a lie, because embeddings still call Google. The literal keys,
quoted verbatim by the build tickets: `EXP-11`

```
Ai:Chat:Provider                  Gemini | AzureFoundry
Ai:Gemini:{Endpoint, Model, ApiKey, EmbeddingModel, Dimensions, QuotaBreakerSeconds, Agents:<agent>}
Ai:AzureFoundry:{Endpoint, Model, ApiKey, Agents:<agent>}
```

`Ai:Gemini` keeps the embedding keys because chat and embeddings genuinely share that endpoint and
key — `EmbeddingOptions.Section` is `"Gemini"` today (`api/Infrastructure/Embeddings/EmbeddingOptions.cs:14`),
and becomes `"Ai:Gemini"`.

**`Ai:AzureFoundry:Model` holds a deployment name**, not a model id. The spelling is shared; the
meaning is per-provider. A deployment name is an operator-chosen string, meaningless outside its
resource — ours is `gpt-4-1-mini`, and the model behind it is `gpt-4.1-mini` 2025-04-14. Documenting
that here is deliberately cheaper than forking the override dictionary into two shapes.

**4. `GEMINI_API_KEY` survives; `AZURE_FOUNDRY_API_KEY` joins it.** `EXP-11`
Both are credential names read explicitly, not config paths bound by the options system, which is
why the ~180-site rename inventory collapses to ~45 files: most sites read the env var and never
touch a `Gemini:` key.

**5. A legacy `Gemini:` section fails loudly at startup.** `EXP-11`
No deprecation window. Nothing external consumes this configuration, and a silently-ignored stale key
is exactly the failure the startup throw exists to prevent.

**6. Unknown provider values fail at startup, in every environment.** `EXP-10`
Missing-credential failure stays Production-only, and since `EXP-18` it asks for the *active*
provider's key (`ChatProviderStartupGuard`, called from `api/Agents/Program.cs`). The two
are different mistakes: a typo'd provider name is wrong everywhere; a missing key is a normal
condition in dev.

**7. Do not attach the ServiceDefaults resilience handler — on either provider.** `EXP-8`
It becomes attachable on Azure, and it is a foot-gun: `AddStandardResilienceHandler` defaults to a
**10 s per-attempt timeout**, which cancels healthy tool-calling completions, and its retries stack
on the SDK's own.

**8. Content filtering is normalized into one typed failure in the shared decorator stack.** `EXP-10`
Azure has two filter shapes — a filtered *prompt* is an HTTP 400 with `code: "content_filter"`, a
filtered *completion* is a 200 with `finish_reason: "content_filter"` and possibly no content — and
Gemini has its own safety-block shapes. Orchestration degrades a failed stage rather than failing the
call, so letting two provider-specific shapes leak into it would put provider knowledge in the one
layer that must have none.

**9. Strict structured output stays off on both providers.** `EXP-10`
`json_schema` + `strict: true` is available on Azure and the probe confirmed it is honoured (§3), but
turning it on is a behaviour change to every agent's parsing path and belongs to its own effort, not
to a plumbing change.

**10. Per-agent overrides live inside the active provider's block.** `EXP-10`
`Ai:Gemini:Agents:<agent>` and `Ai:AzureFoundry:Agents:<agent>`. This is what makes per-agent
*provider* routing mechanically free later without deciding it now (§9).

**11. A nullable `Provider` string column on `AgentUsage`, no backfill.** `EXP-12`
Written as `provider.ToString()`: an enum at the config edge so an unknown value throws at startup, a
string in the database. Because no enum property reaches the EF model, `AppDbContext`'s
enums-persist-by-name conversion loop never sees it. Null means "written before providers existed" —
the convention `LatencyMs`, `Iterations` and `ToolSequence` already use.

The counter-case was put explicitly and rejected: model ids already differ between providers, and
nothing reads either field. But model ids drift across aliases and version suffixes, and deriving
provider by parsing a model string fails exactly when cost attribution starts to matter.

This also **deletes** `UsageMeter`'s config fallback (`api/Agents/Usage/UsageMeter.cs:31-34`), which
would otherwise have been migrated into a new set of key names while staying, in its own comment's
words, something that "mislabels whenever config and reality drift".

**12. The Art. 15(1)(c) recipient category becomes provider-derived.** `EXP-7`
`api/Application/Compliance/Art15Disclosure.cs:62-65` currently hard-codes
`"Google (Gemini), as our AI model provider"`. That string is **not documentation**: it is served
verbatim by `GET /api/me/access` and rendered on the expert's privacy page, which is fully
server-driven — it is the only source of the provider name a data subject ever sees. Ship Azure with
that literal in place and the service names the wrong recipient to a data subject, which is precisely
the mitigation the DPIA cites for risk R7.

Because **embeddings stay on Google regardless of the chat provider**, the disclosure under
`AzureFoundry` names *two* recipients, not one substituted for the other: Google for the embeddings
that power semantic search, and Microsoft Azure for the chat scoring. The `outside this company`
floor stays in both cases.

## 3. What the probe actually observed

`EXP-13` — `tests/Agents.Tests/AzureFoundryDialectProbeTests.cs`, a committed `Category=live` probe
that skips without `AZURE_FOUNDRY_API_KEY`. It runs the bare client: **no transport handler, no
per-call policy**, so a needed shim would surface as a red test rather than as a surprise during the
build. Three measurements, all green on the first run against the real deployment:

| Claim the docs made | What the run showed |
|---|---|
| No `GeminiThoughtSignaturePolicy` analog is needed | **Confirmed.** A replayed history — user → assistant tool call → tool result → assistant answer — was accepted with the tools still declared, and the model called the tool again. This is the exact request Gemini 400s without the policy. |
| No `GeminiCompatHandler` analog is needed | **Confirmed.** `finish_reason` came back `tool_calls` and `stop`, values already in the SDK enum and in this repo's `KnownFinishReasons`. Nothing needed normalizing. |
| `json_schema` + `strict: true` is honoured | **Confirmed, strictly.** Right enum member, right integer, and exactly the declared keys — `additionalProperties: false` enforced, not suggested. |
| *(open in the research)* Which auth the SDK path needs | **`ApiKeyCredential` works.** The SDK sends `Authorization: Bearer <key>`; the v1 endpoint accepts it. Provisioning had only proved a raw `api-key` header, so this closes a real risk: a header swap would have been a transport handler by another name. |
| *(inferred from code in EXP-12)* Metering records the real model | **Observed.** `ModelId` is `gpt-4.1-mini-2025-04-14` on every call, never the deployment name `gpt-4-1-mini`. Now pinned by an assertion in the probe. |

**Cost of the whole exercise:** six calls, 558 prompt + 100 completion tokens, **~$0.0004**. Total
spend across provisioning and proving the dialect is under one cent against the map's $5 ceiling.

**Content filtering was not tripped**, and was not hunted for. Both shapes are handled in the probe
so a future run reports them instead of being surprised.

## 4. Why not Entra ID now

API key first. `Azure.Identity` + `DefaultAzureCredential` is the documented path and the options are
shaped to take a credential later, but Entra is mandatory only for Foundry Agents / Evaluations /
Toolbox — none of which this repo uses — and adopting it now drags a local-dev and CI credential story
into a plumbing change. Named as a later move, not a rejected one.

## 5. Why Azure OpenAI deployments only

Not the Foundry model catalog (Llama, Mistral, DeepSeek, Grok served serverless): different SDK stack
and a different function-calling story per model. The seam targets Azure OpenAI deployments, which
speak the dialect this repo already speaks.

**Not any `gpt-5.6-*` model**, which is the sharpest constraint on this decision and the one most
likely to force the next: every `gpt-5.6-*` model **hard-fails a Chat Completions request carrying
`tools`** — *"Function tools with reasoning_effort are not supported … use /v1/responses or set
reasoning_effort to 'none'"* — and it fires without sending `reasoning_effort` at all, because the
default is `medium`. `AsIChatClient()` speaks Chat Completions, so the newest and longest-lived
Foundry models are unreachable as this repo uses them. `gpt-4.1-mini` is Legacy but alive to
**2027-04-14**.

## 6. Why no failover

This destination is either/or selection. Failover between providers when the Gemini free tier
exhausts wants budget, retry and breaker semantics that nothing here has decided — and the motivation
is real (the repo pins `flash-lite` precisely because its RPD is 500 against every Flash-proper row's
20). It stays a live question (§9), not a rejected one.

## 7. Region, and the residency claim this does **not** support

The deployment is `GlobalStandard` in Sweden Central. **Inference is not confined to the EU.** `EXP-9`

The subscription is at quota Tier 0, whose ledger carries `OpenAI.GlobalStandard.gpt4.1-mini` at 200
and `OpenAI.DataZoneStandard.gpt4.1-mini` at **0** — EU-confined inference is not purchasable on this
subscription today, and pre-emptive quota requests are documented as likely denied. Region was never
the constraint; the deployment type was.

Two consequences, stated rather than implied:

- **The map's constraint 10 is unmet.** Choosing an EU region did not buy residency.
- **DPIA risk R7 stays open** and is not improved by this deployment. The compliance audit found that
  there is no written transfer or residency claim anywhere in the repo to break — every hit is in the
  DPIA and every one says the question is open: `manuals/dpia-expert-workspace.md:67-71` ("This
  leaves the company", twice), `:89-96` ("Not assessed here, and it needs to be") and `:197` (R7,
  "Open") — so nothing is contradicted. But nothing is gained either. The honest edit is a
  per-provider row in DPIA §1, with R7 downgraded to "assessed for one provider, open for the other".

Revisit if the tier lifts. It upgrades on Microsoft's schedule, not on request.

## 8. What this supersedes, and what it does not

**Superseded: the per-agent provider-profile plan.** `manuals/cloudflare-workers-ai-provider.md`
sketched widening configuration to per-agent provider profiles (`agent → { endpoint, apiKey, model }`)
if its gate passed. This ADR decides a different shape: a **global** `Ai:Chat:Provider` with per-agent
*model* overrides inside the active provider's block. The profile shape is not adopted.

**Not superseded: Cloudflare Workers AI as an option.** That manual is **still live, and un-gated** —
not stale, not rejected. Its gate prototype landed as a skipped `Category=live` probe
(`tests/Agents.Tests/CloudflareWorkersAiGateTests.cs`) and **has never been run**, because no
Cloudflare key exists. Nothing about that provider has been measured or ruled out; its motivation is
this ADR's failover fog (§6, §9). Read the manual as an unfinished measurement, not as history.

A reader arriving at that file should be able to tell those two halves apart, which is why the build
tickets include a one-paragraph header edit pointing here.

## 9. What would make us revisit

- **The seam moves to the Responses API.** Then the thought-signature question re-opens on the Azure
  side: encrypted reasoning items are a Responses-API feature, and the probe measured Chat Completions
  only. This is also the route to the `gpt-5.6-*` models (§5).
- **The subscription's quota tier lifts** — `DataZoneStandard` becomes purchasable and the residency
  answer in §7 changes.
- **A Cloudflare key appears** and its gate can finally run.
- **Failover, per-agent provider routing, or provider-aware budget caps** get evidence behind them. A
  token cap tuned to a free tier means something different when tokens cost money.
- **Embeddings need a second provider.** Out of scope here for a concrete reason: the pgvector column
  fixes 1536 dimensions and the roster is already embedded, so a second backend means dimension
  matching or a full re-embed migration. Different blast radius, its own effort.
- **The Art. 13(1)(e) transparency gap gets closed.** The notice's own "Who sees it" section
  (`api/Application/Compliance/TransparencyNotice.cs:92`) names no provider at all — a gap that predates this effort, recorded in
  `manuals/transparency-and-export.md:53-58`. Closing it needs a `CurrentVersion` bump re-acknowledged
  by every account, which is a re-consent flow, deliberately not dragged into a plumbing change.

## 10. The build tickets

Eight tickets, `ready-for-agent`, ordered by `blockedBy`. The first is the safety net for everything
after it.

| # | Ticket | | Blocked by |
|---|---|---|---|
| 1 | Migrate the `Gemini:*` config keys to the `Ai:*` shape | `EXP-15` | — |
| 2 | The seam: `AddChatProvider`, with Gemini as the only construction branch | `EXP-16` | 1 |
| 3 | The Azure construction branch | `EXP-17` | 2 |
| 4 | The Production credential guard becomes provider-aware | `EXP-18` | 3 |
| 5 | Usage rows record which provider served the run | `EXP-19` | 2 |
| 6 | Normalize content filtering into one typed failure | `EXP-20` | 3 |
| 7 | The Art. 15 recipient category names the configured chat provider | `EXP-21` | 3 |
| 8 | Provider naming in the compliance prose (documentation only) | `EXP-22` | 7 |

The rename lands **first**, so the seam is written against the final key names instead of migrating
them underneath itself. Tickets 4–7 fan out from the Azure branch and can land in any order.

The live probe that this map also expected as a build ticket is **already landed** — EXP-13, merged
before this ADR was written — so it does not appear above.
