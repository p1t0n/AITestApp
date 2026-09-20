# Which EU Azure OpenAI deployment serves all eight agents, and what it costs (research)

Verified against live Microsoft docs and the public Azure Retail Prices API on 2026-09-20. P1T-242,
under the map P1T-241.

**Headline: the region is nearly free to choose and the model is nearly free to run — the two
things that actually constrain this are a new subscription's quota tier and the reasoning-model
tool-calling trap. Global Standard costs exactly the same dollar in Sweden Central as in East US,
so "EU region" buys residency, not a bill; the EU premium is a property of the *deployment type*
(Data Zone +10%, regional Standard +21%), not of the region. But a subscription at the bottom quota
tier is granted quota for exactly three chat models — `gpt-4.1-mini`, `gpt-5-mini`, `o4-mini` — and
only on Global Standard, which is the one type that does *not* keep inference in the EU. And every
`gpt-5.6-*` model hard-fails a Chat Completions request that carries `tools`, which is what all
eight of our agents send. `gpt-4.1-mini` on Data Zone Standard in Sweden Central threads all of it:
tool calling and `json_schema` on the API we already speak, EU-boundary processing, no idle charge,
and ~$1.76 for a thousand test calls.**

No Azure resource was created, modified or read during this research. Every number below comes from
a page or API response fetched today; the "Source" column names it.

---

## 0. The docs moved — old URLs are stale

Every `learn.microsoft.com/azure/ai-foundry/...` URL now redirects, and the product is branded
**Microsoft Foundry**, not Azure AI Foundry. Two pages named in the issue no longer exist.

| Old path | Current canonical | Source |
|---|---|---|
| `/azure/ai-foundry/openai/concepts/models` | [`/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure`](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure) | fetched, `ms.date` 2026-09-04 |
| `/azure/ai-foundry/openai/concepts/model-matrix` | **404 — deleted.** Replaced by [region-availability](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability) | fetched, `ms.date` 2026-09-03 |
| `/azure/ai-foundry/openai/how-to/deployment-types` | [`/azure/foundry/foundry-models/concepts/deployment-types`](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/deployment-types) | fetched, `ms.date` 2026-08-06 |
| `/azure/ai-foundry/openai/concepts/data-residency` | **404 — deleted.** Residency now lives in deployment-types + [data-privacy](https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/openai/data-privacy) | fetched |
| `/azure/ai-foundry/openai/quotas-limits` | [`/azure/foundry/openai/quotas-limits`](https://learn.microsoft.com/en-us/azure/foundry/openai/quotas-limits) | fetched, `ms.date` 2026-08-20 |
| `/azure/ai-foundry/openai/how-to/create-resource` | [`/azure/foundry-classic/openai/how-to/create-resource`](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/how-to/create-resource) — header: *"Applies only to: Foundry (classic) portal"* | fetched |

Anything under `/azure/foundry-classic/` is the legacy doc set. Cite `/azure/foundry/`.

A second structural change: **the function-calling page no longer lists models or api-versions.**
Its "Function calling support" section now reads only *"Support varies by model, API, deployment
type, and model version"* and points at the catalogue
([function-calling](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/function-calling)).
There is no single authoritative tool-calling-by-model table any more; the per-model capability
blurbs plus the [reasoning](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning)
feature matrix are what remain.

---

## 1. Which EU-deployable chat models do tool calling **and** `json_schema`

All eight agents need both. Tool callers: `RosterQaAgent.cs:97` (`ChatToolMode.RequireAny`),
`MatchAgent.cs:70`, `ResumeIngestionAgent.cs:131`. Schema-constrained:
`RosterScan/ScoringTransport.cs:117`, `ShortlistAgent.cs:89`, `JdRequirementExtractor.cs:64`,
`Staffing/StaffingPipeline.cs:613`.

SE = Sweden Central · WE = West Europe · FR = France Central · CH-N = Switzerland North.
GS = `GlobalStandard` · DZS = `DataZoneStandard` · Std = `Standard` (regional).

| Model (version) | GS in SE/WE/FR/CH-N | DZS | Std (regional) | Tools | `json_schema` | Retires | Source |
|---|---|---|---|---|---|---|---|
| `gpt-4.1-mini` (2025-04-14) | all 4 | SE, WE, FR | **SE, WE, FR, CH-N** | ✅ | ✅ (chat completions) | 2027-04-14, Legacy | [region-availability](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability), [retirements](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/model-retirement-schedule) |
| `gpt-4.1-nano` (2025-04-14) | all 4 | SE, WE, FR | none in EU | ✅ | ✅ | 2027-04-14, Legacy | same |
| `gpt-5-mini` (2025-08-07) | all 4 | SE, WE, FR | **none anywhere** | ✅ | ✅ | 2027-02-09, GA | same |
| `gpt-5-nano` (2025-08-07) | all 4 | SE, WE, FR | none anywhere | ✅ | ✅ | 2027-02-09, GA | same |
| `gpt-4o-mini` (2024-07-18) | all 4 | SE, WE, FR | SE only | ✅ | ⚠️ see §1.1 | 2027-04-14, **Deprecated** | same |
| `gpt-5.4-mini` (2026-03-17) | all 4 | all 4 | none | ✅ | ✅ | 2027-09-21, GA | same |
| `gpt-5.4-nano` (2026-03-17) | all 4 | **not listed** | none | ✅ | ✅ | 2027-09-21, GA | same |
| `gpt-5.6-sol / -terra / -luna` | all 4 | all 4 | none | ⛔ **not with Chat Completions** — §1.2 | ✅ | 2028-01-11, GA | [reasoning](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning) |
| `o4-mini`, `o3-mini`, `o3`, `o1` | all 4 | SE, WE, FR | varies | ✅ | ✅ | **2026-11-19 — 2 months** | [retirements](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/model-retirement-schedule) |
| `gpt-4o` (2024-05-13) | all 4 | SE, WE, FR | SE only | ✅ | ❌ **no json_schema** | **2026-10-01 — 11 days** | same |

Poland Central, Norway East, Germany West Central and Spain Central match France Central on Global
Standard for every row. They diverge elsewhere: Norway East and Switzerland North are excluded from
Data Zone Standard for everything older than `gpt-5.4`, and Germany West Central / Poland Central /
Spain Central carry **no chat models at all** in the EU regional-Standard table (embeddings only).

### 1.1 Two doc contradictions worth knowing

- **`gpt-4o-mini` structured outputs.** The
  [structured-outputs](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs)
  page's supported-model list names `` `gpt-4o-mini` version `2024-07-18` `` explicitly. The models
  page blurb for the same model lists only *"JSON Mode. Parallel function calling"* — no
  "Structured outputs" — while its `gpt-4o` neighbours do say it. The feature's own doc is the
  better source, but this is a contradiction, not a settled fact.
- **`o3` and `gpt-5.4-mini` parallel tool calls.** The models page says both support *"parallel
  tool calling"*; the reasoning-page matrix shows `-` for both. The reasoning matrix shows `-` for
  **every** o-series model (`o1`, `o3`, `o3-mini`, `o3-pro`, `o4-mini`, `codex-mini`), so the models
  page reads like a copy-paste slip. Assume no parallel tool calls on o-series.

Mostly moot for us: *"Structured outputs are not supported with parallel function calls. When using
structured outputs set `parallel_tool_calls` to `false`."*
([structured-outputs](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs))
Our schema-constrained agents already send one schema per call.

### 1.2 The `gpt-5.6` trap — this one would break all eight agents

From [reasoning](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning), section
"Tool calling with reasoning models", verbatim:

> The `gpt-5.6` models support the Chat Completions API and tools, but can't combine reasoning with
> tools on Chat Completions. A Chat Completions request that includes `tools` fails with the
> following error:
> ```
> Function tools with reasoning_effort are not supported for gpt-5.6-sol in /v1/chat/completions.
> To use function tools, use /v1/responses or set reasoning_effort to 'none'.
> ```
> The request fails even when you don't send `reasoning_effort`, because these models default to
> `medium`. Sending `tools` is enough to trigger the error.

`Microsoft.Extensions.AI`'s `OpenAIClient.GetChatClient(...).AsIChatClient()` — what
`GeminiServiceCollectionExtensions.cs:53` builds — speaks **Chat Completions**. So the newest and
longest-lived Foundry models are the ones we cannot adopt without either moving the whole agent
layer to the Responses API or pinning `reasoning_effort: "none"` on every tool-bearing call. That
rules `gpt-5.6-*` out of the first slice.

### 1.3 API version

| Fact | Value | Source |
|---|---|---|
| First api-version supporting `response_format: json_schema` | `2024-08-01-preview`; also the v1 GA API | [structured-outputs](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs), "API support" |
| `parallel_tool_calls` request parameter added | `2024-09-01-preview` | [api-version-lifecycle](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle) changelog |
| On the `/openai/v1/` route | *"`api-version` is no longer a required parameter with the v1 GA API"* — both features simply available | [api-version-lifecycle](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle) |
| Max `tools` per `/chat/completions` request | 128 (our largest grant is 4 — `api/Agents/appsettings.json` `McpAuth:roster-qa:Tools`) | [quotas-limits](https://learn.microsoft.com/en-us/azure/foundry/openai/quotas-limits) |

---

## 2. Price per 1M tokens, EU regions

Both public pricing pages render `$-` placeholders to a fetcher — azure.microsoft.com/pricing for
[openai-service](https://azure.microsoft.com/en-us/pricing/details/cognitive-services/openai-service/)
shows no numbers, only the three tier names. Every figure below therefore comes from the **public,
unauthenticated Azure Retail Prices API**, queried today:

```
https://prices.azure.com/api/retail/prices?api-version=2023-01-01-preview&currencyCode=USD
  &$filter=serviceName eq 'Foundry Models' and armRegionName eq 'swedencentral'
```

`serviceName` is **`Foundry Models`**, not `Cognitive Services` — the latter returns zero rows.
Meter vocabulary: `Glbl`/`Gl` = Global Standard, `DZone`/`Dz` = Data Zone Standard, `regnl` =
regional Standard, `cchd`/`cd` = cached input, `pp` = priority processing. USD, list price,
`unitOfMeasure` is `1M` for GPT-5-family meters and `1K` for GPT-4-family meters (converted below).

### 2.1 Global Standard — **identical in every region checked**

Verified meter-by-meter in `swedencentral`, `westeurope`, `francecentral`, `switzerlandnorth` and
`eastus`: the same retail price, to the cent.

| Model | Input $/1M | Cached input $/1M | Output $/1M | Meter (Sweden Central) |
|---|---|---|---|---|
| `gpt-5-nano` | 0.05 | 0.005 | 0.40 | `GPT 5 Nano Inpt Glbl 1M Tokens` etc. |
| `gpt-4.1-nano` | 0.10 | 0.025 | 0.40 | `gpt 4.1 nano Inp glbl Tokens` (×1000) |
| `gpt-4o-mini` | 0.15 | 0.075 | 0.60 | `gpt-4o-mini-0718-Inp-glbl Tokens` (×1000) |
| `gpt-5.4-nano` | 0.20 | 0.02 | 1.25 | `5.4 nano Inp Gl 1M Tokens` |
| **`gpt-5-mini`** | **0.25** | **0.025** | **2.00** | `GPT 5 Mini Inpt Glbl 1M Tokens` |
| **`gpt-4.1-mini`** | **0.40** | **0.10** | **1.60** | `gpt 4.1 mini Inp glbl Tokens` (×1000) |
| `gpt-5.4-mini` | 0.75 | 0.075 | 4.50 | `5.4 mini Inp Gl 1M Tokens` |
| `o4-mini` | 1.10 | 0.275 | 4.40 | `o4 mini ... glbl Tokens` (×1000) |

### 2.2 Data Zone Standard — exactly **+10%**, same everywhere it exists

| Model | Input $/1M | Cached $/1M | Output $/1M | Sweden / West Europe / France Central | Switzerland North |
|---|---|---|---|---|---|
| `gpt-5-nano` | 0.055 | 0.0055 | 0.44 | ✅ same price in all three | **no DZ meter** |
| `gpt-4.1-nano` | 0.11 | 0.028 | 0.44 | ✅ | no DZ meter |
| `gpt-4o-mini` | 0.165 | 0.083 | 0.66 | ✅ | no DZ meter |
| **`gpt-5-mini`** | **0.275** | **0.0275** | **2.20** | ✅ | no DZ meter |
| **`gpt-4.1-mini`** | **0.44** | **0.11** | **1.76** | ✅ | no DZ meter |
| `gpt-5.4-mini` | 0.825 | 0.0825 | 4.95 | ✅ | ✅ |

The ratio is exactly 1.10 on every 1M-denominated meter (`GPT 5 Mini Inpt Glbl` 0.25 →
`GPT 5 Mini Inpt DZone` 0.275; `outpt Glbl` 2.00 → `outpt DZone` 2.20). Switzerland North's full
meter catalogue contains only 13 DataZone rows, all GPT-6 "astra" / `gpt-oss` / provisioned — **no
cheap chat model has a Data Zone meter in Switzerland North**, notwithstanding the deployment-types
page saying the EU zone *"can include EFTA countries … such as Norway and Switzerland"*.

### 2.3 Regional Standard — **the only tier where EU pricing genuinely differs from US**

| Model | Sweden Central | France Central | Switzerland North | West Europe | East US |
|---|---|---|---|---|---|
| `gpt-4.1-mini` in / out | **0.484 / 1.936** | 0.484 / 1.936 | 0.484 / 1.936 | **0.528 / 2.112** | 0.44 / 1.76 |
| `gpt-4.1-nano` in / out | 0.121 / 0.484 | — | — | — | 0.11 / 0.44 |
| `gpt-4o-mini` in / out | 0.165 / 0.66 | — | — | — | 0.165 / 0.66 |
| `gpt-5-mini`, `gpt-5-nano`, `gpt-5.4-*` | **no regional meter in any Azure region** | | | | |

Ratio to Global Standard: **East US regional ×1.10 · Sweden / France / Switzerland regional ×1.21 ·
West Europe regional ×1.32.** West Europe is the most expensive of the four EU regions and the only
one that differs from its neighbours, so "EU region" is not one price — Sweden Central and France
Central are ~9% cheaper than West Europe on this tier. Query used:
`$filter=serviceName eq 'Foundry Models' and armRegionName eq 'westeurope' and meterName eq
'gpt 4.1 mini Inp regnl Tokens'` → `retailPrice 0.000528`, `unitOfMeasure "1K"`,
`effectiveStartDate 2025-11-01`.

### 2.4 Other tiers, from the same API

| Tier | Multiple of Global Standard | Note |
|---|---|---|
| Global Batch / Data Zone Batch | **×0.50** | 24-hour target turnaround, separate enqueued-token quota |
| Flex (Global only, newest models) | ×0.50 | e.g. `54 nano Inp Flex Gl 1M Tokens` 0.10 vs Standard 0.20 |
| Priority processing (`pp`) | ~×1.8 | `5 mini pp Inp Gl 1M Tokens` 0.45 vs 0.25 |

---

## 3. Deployment types: residency, idle charge, and the right default

Table verbatim from
[deployment-types](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/deployment-types)
(`ms.date` 2026-08-06):

| Deployment type | SKU code | Data processing | Billing | EU-resident inference? | Idle charge? |
|---|---|---|---|---|---|
| Global Standard | `GlobalStandard` | Any Azure region | Pay-per-token | **No** | No |
| Data Zone Standard | `DataZoneStandard` | Within data zone (US / EU / APAC) | Pay-per-token | **Yes** | No |
| Standard | `Standard` | Within Azure geography | Pay-per-token | **Yes, tightest** | No |
| Global Batch | `GlobalBatch` | Any Azure region | 50% discount, 24-hr | No | No |
| Data Zone Batch | `DataZoneBatch` | Within data zone | 50% discount | Yes | No |
| Global Provisioned | `GlobalProvisionedManaged` | Any Azure region | Reserved PTU | No | **Yes** |
| Data Zone Provisioned | `DataZoneProvisionedManaged` | Within data zone | Reserved PTU | Yes | **Yes** |
| Regional Provisioned | `ProvisionedManaged` | Within Azure geography | Reserved PTU | Yes | **Yes** |
| Developer | `DeveloperTier` | Any Azure region | Pay-per-token | **No guarantee** | No |

The governing callout, verbatim:

> **Data residency for all deployment types**: Data stored at rest remains in the designated Azure
> geography. However, inferencing data is processed as follows:
> - **Global** types: May be processed in any Azure region
> - **Data Zone** types: The service processes data only within the Microsoft-specified data zone
>   (US, EU, or Asia Pacific (APAC)).
> - **Standard and Regional Provisioned** types: Prompts and responses are processed within the
>   customer-specified Azure geography and might be processed between regions within that geography
>   for operational purposes.

So **at-rest is always in your geography for every type; only *processing* location varies.** Two
riders worth carrying into the DPIA: the EU zone *"follows the Azure EU Data Boundary, which can
include European Free Trade Association (EFTA) countries … such as Norway and Switzerland"*, and
*"Microsoft can add regions to either data zone without prior notice"*. The narrower
[data-privacy](https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/openai/data-privacy)
wording says *"that or any other European Union Member Nation"*. Same page adds, usefully: *"For
Models sold by Azure deployed in the European Economic Area, the authorized Microsoft employees
[doing abuse-monitoring human review] are located in the European Economic Area."*

### 3.1 Idle charge — the one thing that can eat $5 while nobody is looking

From [provisioned-throughput](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/provisioned-throughput), verbatim:

> All provisioned deployment types are billed at an hourly rate ($/PTU/hr) based on the number of
> PTUs deployed, **regardless of the number of tokens consumed. The meter starts when the deployment
> is created and stops when it's deleted.**

**Never create any `*ProvisionedManaged` deployment on this map.** Everything else in §3 is pure
pay-per-token with no standing charge, and the Azure OpenAI / Foundry resource itself (S0) bills
nothing until a deployment serves a token. The `$1.70/hour` hosting fee that appears in the pricing
data (`gpt-4.1-mini-ft hosting global Unit`, per 1 Hour) applies **only to fine-tuned** model
deployments — [fine-tuning-cost-management](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/fine-tuning-cost-management) —
and we are not fine-tuning.

The docs' own default, verbatim: *"For most workloads, start with **Global Standard**. It launches
first when a new model releases, has the lowest price, and offers the broadest region coverage."*
For a $5 experiment that also wants EU processing, the answer is **Data Zone Standard** — same
pay-per-token shape, same absence of idle charge, +10%.

---

## 4. Quota on a new subscription

Two structural changes landed in 2026 that invalidate most older guidance.

| Fact | Value | Source |
|---|---|---|
| Quota is **subscription-scoped**, not per-resource / per-region, since **2026-05-07** | Global Standard: one pool across all regions. Data Zone Standard: one pool per data zone (US / EU) | [quotas-limits](https://learn.microsoft.com/en-us/azure/foundry/openai/quotas-limits) |
| Default/Enterprise levels replaced by **quota tiers** | *"Seven tiers are available: Free Tier and Tiers 1 through 6"*; initial tier *"based on their current usage of that model and their current relationship with Microsoft, such as Enterprise Agreement (EA or MCA-E) status"* | same |
| Tiers auto-upgrade with consumption; opt out with `tierUpgradePolicy: "NoAutoUpgrade"` | preview | same |
| Azure OpenAI resources per subscription | **30** (see contradiction below) | same, reference table |
| Max standard deployments per resource | 32 | same |
| Max `/chat/completions` tools | 128 | same |

### 4.1 The bottom tier is three chat models wide

The tab labelled **Tier 0** (the "Free Tier" the prose names — the tables are ordered 1→6 then 0
last, which is easy to miss) is, in full:

| Model | Deployment type | RPM | TPM | Source |
|---|---|---|---|---|
| `gpt-4.1-mini` | `GlobalStandard` | 200 | 200,000 | [quotas-limits](https://learn.microsoft.com/en-us/azure/foundry/openai/quotas-limits), Tier 0 tab |
| `gpt-5-mini` | `GlobalStandard` | 500 | 500,000 | same |
| `o4-mini` | `GlobalStandard` | 100 | 100,000 | same |
| `text-embedding-3-small` | `GlobalStandard` | 1000 / 10s | 1,000,000 | same |

**No Data Zone Standard row. No regional Standard row. Three chat models.** That is the single
constraint most likely to surprise whoever provisions this: at the bottom tier the only thing you
can deploy is the one deployment type that does *not* keep inference in the EU.

Tier 1 — the realistic floor once a subscription has any spend history — opens it up:

| Model | Deployment type | RPM | TPM |
|---|---|---|---|
| `gpt-4.1-mini` | `GlobalStandard` | 5,000 | 5,000,000 |
| `gpt-4.1-mini` | **`DataZoneStandard`** | 2,000 | 2,000,000 |
| `gpt-4.1-mini` | **`Standard`** | 6,000 | 6,000,000 |
| `gpt-5-mini` | `GlobalStandard` | 1,000 | 1,000,000 |
| `gpt-5-mini` | `DataZoneStandard` | 300 | 300,000 |
| `gpt-4.1-nano` / `gpt-5-nano` | `GlobalStandard` | 5,000 | 5,000,000 |

Source: [quotas-limits](https://learn.microsoft.com/en-us/azure/foundry/openai/quotas-limits),
Tier 1 tab. Note `gpt-5-mini` and `gpt-4o-mini` have **no `Standard` row in any tier** —
`gpt-4.1-mini` is the only cheap chat model with a regional-Standard escape hatch.

**Is a quota request needed before a first deployment? No.** *"When you onboard a subscription to
Azure OpenAI, you receive default quota for most available models"*
([quota how-to](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/how-to/quota)).
Even Tier 0's 200,000 TPM is orders of magnitude past a $5 experiment. And asking pre-emptively is
counterproductive: *"priority goes to customers who actively use their existing quota allocation.
Requests that don't meet this condition might be denied."*

**Check the tier before provisioning** — this is a read-only control-plane GET, no writes:

```bash
az rest --method get --url "https://management.azure.com/subscriptions/<SUB_ID>/providers/Microsoft.CognitiveServices/quotaTiers?api-version=2025-10-01-preview"
```

Returns `properties.currentTierName`. If it says Tier 0, Data Zone Standard is not available yet and
the first smoke test runs on Global Standard.

### 4.2 RPM↔TPM is per-model, not 6:1

The old *"6 RPM per 1,000 TPM"* rule is legacy and applies only to older chat models. Back-computed
from the tier tables: `gpt-5-mini` and `gpt-4.1-mini` are **1 RPM per 1,000 TPM**; `gpt-4o-mini` is
**10 RPM per 1,000 TPM**. The docs warn: *"This is particularly important for programmatic model
deployment as changes in RPM/TPM ratio can result in accidental misallocation of quota."* Read the
row, don't assume.

Two traps for a small-budget run that will look like quota bugs:

- **429s below your quota are documented and expected.** *"Standard (pay-as-you-go) deployments
  share a resource pool. When demand approaches capacity limits, the system temporarily reduces your
  deployment's effective rate limit."* Detect by comparing `x-ratelimit-limit-tokens` in the
  response header against configured TPM. Do not "fix" it by requesting quota.
- **Rate-limit accounting charges `max_tokens`, not actual output.** *"if you expect responses of
  about 200 tokens, don't set `max_tokens` to 4,000."* Our agents set generous ceilings.

### 4.3 One contradiction, flagged

The quotas-limits reference table says "Azure OpenAI resources per Azure **subscription** | 30", but
the [classic quota page](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/how-to/quota)
and the upgrade-troubleshooting table both say **per subscription per region** (and give
`AIServices` a limit of 100, not 30). Two sources and a concrete error message favour the per-region
reading. Also: deleting a resource while deployments still exist **strands its quota for 48 hours**
until purge — delete deployments first.

---

## 5. Is a Foundry *project* required? No.

| Question | Answer | Source |
|---|---|---|
| Does chat completions need a Foundry project? | **No.** *"An Azure OpenAI resource provides only the `/openai/v1` endpoint"* — which is all we need | [sdk-overview](https://learn.microsoft.com/en-us/azure/foundry/how-to/develop/sdk-overview) |
| Is a plain Azure OpenAI resource enough? | *"If your workload only requires Azure OpenAI completions without agent hosting or evaluation, a standalone Azure OpenAI resource might be sufficient."* | [architecture](https://learn.microsoft.com/en-us/azure/foundry/concepts/architecture) |
| What is a project, then? | A **subresource**, `Microsoft.CognitiveServices/accounts/projects`, kind `AIServices` — a development/isolation boundary, not an inference prerequisite | same |
| Hub-based vs Foundry project? | Resolved: hub-based survives only in `foundry-classic`. Current docs know only Foundry resource → Foundry project | same |
| Is the standalone Azure OpenAI resource deprecated? | **No date published.** *"Both services are generally available and supported before and after the upgrade. Upgrading your Azure OpenAI resource is an opt-in capability."* But some resources are being **auto-upgraded on Microsoft's schedule** | [upgrade-azure-openai](https://learn.microsoft.com/en-us/azure/foundry/how-to/upgrade-azure-openai) |
| What *is* dated | Assistants API retires **2026-08-26**; Agents (classic) retire **2027-03-31**. API retirements, not resource-type retirements — we use neither | same |

### 5.1 Endpoint shapes and where the key lives

| Shape | Auth | Notes | Source |
|---|---|---|---|
| `https://<name>.openai.azure.com/openai/deployments/<dep>/chat/completions?api-version=...` | `api-key` header | Classic. Deployment in the **path** | [reference](https://learn.microsoft.com/en-us/azure/foundry/openai/reference) |
| **`https://<name>.openai.azure.com/openai/v1/`** | **`api-key` / `ApiKeyCredential`** | **v1 GA. Deployment goes in the `model` field. No `api-version`.** | [api-version-lifecycle](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle) |
| `https://<name>.services.ai.azure.com/openai/v1/` | same | Equivalent; *"`base_url` accepts both … formats"* | same |
| `https://<name>.services.ai.azure.com/api/projects/<proj>` | Entra only in practice | Foundry **project** endpoint — SDK / agents / evaluations. **Not** an OpenAI-compatible base URL | [sdk-overview](https://learn.microsoft.com/en-us/azure/foundry/how-to/develop/sdk-overview) |

Key auth by surface, from
[authentication-authorization](https://learn.microsoft.com/en-us/azure/foundry/concepts/authentication-authorization-foundry):
basic model inference (chat, embeddings) **Yes**; Agents service **No**; Evaluations **No**; Toolbox
**No**. We only need inference, so the API-key-first constraint in P1T-241 holds with no caveat. A
custom subdomain is required if we later switch to token-based auth.

### 5.2 What this means for `api/Agents` — the wiring is a two-line change

Current wiring builds one shared `OpenAIClient` from an endpoint + `ApiKeyCredential`
(`api/Agents/Configuration/GeminiServiceCollectionExtensions.cs:32-41`), then
`GetChatClient(model).AsIChatClient()` per agent. The documented Azure v1 C# form is the *same
shape*, minus the two Gemini shims:

```csharp
OpenAIClient client = new(
    new ApiKeyCredential("{your-api-key}"),
    new OpenAIClientOptions
    {
        Endpoint = new("https://YOUR-RESOURCE-NAME.openai.azure.com/openai/v1/"),
    });
```

Source: [api-version-lifecycle](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle),
"Code changes → C# → v1 API → API Key".

Three things the spec must say out loud:

1. **`model` carries the *deployment name*, not the model id.** *"Azure OpenAI always requires
   deployment name, even when using the model parameter"*
   ([create-resource](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/create-resource)).
   Our per-agent override (`AgentsOptions.cs` → `GeminiOptions.Agents`, `agent → model`) maps onto
   `agent → deployment` unchanged, which is why per-agent routing falls out free.
2. **No `GeminiCompatHandler`, no `GeminiThoughtSignaturePolicy`.** P1T-241 constraint 8 is
   satisfied by simply not attaching them — the Azure factory builds a bare `OpenAIClient`.
3. **Troubleshooting note worth pinning in the ADR:** *"For a `404` response, confirm that the base
   URL ends in `/openai/v1/` and that `model` contains a valid deployment name."*

One documented contradiction on this route: the
[endpoints](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/endpoints) page,
under a heading titled "Use API key authentication", says to pass the key as
`Authorization: Bearer`; api-version-lifecycle shows `api-key:`. The SDK path is unambiguous
(`ApiKeyCredential`), so this only bites hand-rolled curl.

---

## 6. What a thousand test calls costs

No repo-wide "cost per call" exists, so two anchors. The first is a generic tool-calling turn
(2,000 input + 500 output tokens). The second is this repo's own **measured** figure for
`roster-scan`, the code-driven agent that sends a `json_schema` on every call —
**5,112 tokens/call average** (`manuals/agent-cost-budgets.md` §1.2), split 90/10 input/output,
which matches its shape: 10 expert digests + the JD + the schema in, a scored list out.

| Model · deployment · Sweden Central | 1,000 × (2k in + 0.5k out) | 1,000 × roster-scan (4.6M in + 0.51M out) |
|---|---|---|
| `gpt-5-nano` · Global Standard | **$0.30** | $0.43 |
| `gpt-4.1-nano` · Global Standard | $0.40 | $0.66 |
| `gpt-4o-mini` · Global Standard | $0.60 | $1.00 |
| `gpt-5-mini` · Global Standard | $1.50 | $2.17 |
| **`gpt-5-mini` · Data Zone Standard** | **$1.65** | **$2.39** |
| `gpt-4.1-mini` · Global Standard | $1.60 | $2.66 |
| **`gpt-4.1-mini` · Data Zone Standard** | **$1.76** | **$2.92** |
| `gpt-4.1-mini` · regional Standard | $1.94 | $3.22 |
| `gpt-5.4-mini` · Data Zone Standard | $4.13 | $6.32 — **over budget** |

Arithmetic from §2 rates; e.g. `gpt-4.1-mini` DZS = 2.0M × $0.44 + 0.5M × $1.76 = $0.88 + $0.88.

Three notes on reading this table:

- **`gpt-5-mini` looks cheaper than it is.** It is a reasoning model: billed output includes hidden
  reasoning tokens, and its output meter ($2.20/1M DZS) is 25% above `gpt-4.1-mini`'s ($1.76). The
  columns above assume a fixed output size, which is exactly the assumption reasoning breaks. For a
  fixed $5 ceiling, the non-reasoning model is the predictable one.
- **The nano tier is not reachable at Tier 0.** `gpt-5-nano` and `gpt-4.1-nano` are the cheapest
  qualifying rows by a wide margin, but neither appears in the Tier 0 quota table — they need
  Tier 1+ or a quota request.
- **Cached input is 4× cheaper** ($0.11 vs $0.44/1M on `gpt-4.1-mini` DZS). Our agents resend a
  stable system prompt and schema on every call, so real spend should land under these figures.

---

## 7. Recommendation

**Sweden Central · `gpt-4.1-mini` (2025-04-14) · `DataZoneStandard` — ~$1.76 for 1,000 test calls
at 2k in / 500 out, ~$2.92 at this repo's measured roster-scan size; fall back to `GlobalStandard`
(~$1.60 / ~$2.66) if the subscription is still at quota Tier 0, where Data Zone quota does not
exist.**

Why this row and not a cheaper one: it is the only cheap chat model that has Tier-0 quota **and** a
Data Zone deployment **and** a regional-Standard escape hatch in Sweden Central if strict in-region
processing is ever required; it does tool calling and `json_schema` on **Chat Completions**, which
is the API `Microsoft.Extensions.AI` already speaks, so no Responses-API migration and none of the
`gpt-5.6` trap; and it is not a reasoning model, so a token budget means what it says. Sweden
Central over West Europe because West Europe's regional-Standard tier is ~9% dearer and buys
nothing; over Switzerland North because Switzerland North has no Data Zone meter for any cheap chat
model. The two things to confirm before spending anything: the subscription's
`currentTierName` (§4.1, read-only GET), and that nothing in the plan creates a
`*ProvisionedManaged` deployment (§3.1).
