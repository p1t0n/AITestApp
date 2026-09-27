# The embedder against Azure OpenAI's v1 endpoint: what works unchanged (research, EXP-53)

Research for map EXP-52 (*embeddings on Azure OpenAI — Google out, provider selected by
configuration*). The question: `AddGeminiEmbeddings` builds a plain `OpenAIClient` with an
`ApiKeyCredential` and a custom `Endpoint`, calls `GetEmbeddingClient(model).AsIEmbeddingGenerator()`,
and `GeminiEmbedder` passes `Dimensions` on every request. Point it at
`https://experttojob-openai-swc.openai.azure.com/openai/v1/` with a `text-embedding-3-small`
deployment — what works unchanged, and what doesn't?

Researched 2026-09-27 from Microsoft Learn, the `openai-dotnet`, `dotnet/extensions` and
`System.ClientModel` sources at the versions this repo actually resolves, and this repo. **No Azure
call was made, no key was read, and nothing in Azure was touched.** Every "observed" claim below is
either a doc/source quote or an observation the chat path already made (ADR §3); nothing here is a
new measurement against the real deployment.

**Headline: the client construction works unchanged — same class, same credential, same call — and
so does the request shape. What does *not* carry over is the Gemini-shaped policy around it: the key
lookup (`GEMINI_API_KEY` always wins), the config section (`Ai:Gemini`), and the quota breaker, which
reads every exhausted 429 as a *daily* cap and then takes semantic search down to lexical fallback
for 30 minutes. On Azure a 429 is a per-minute / per-10-second throttle, and the Tier 0 embedding
quota (1,000 RPM / 1M TPM) is two orders of magnitude above anything this roster sends. No package
bump is needed.**

## Answers at a glance

| Question | Answer | Confidence |
|---|---|---|
| Auth | The SDK sends `Authorization: Bearer <key>`. The v1 spec accepts the key in `api-key` **or** `authorization`; the chat path observed Bearer working on this same resource. No header shim. | Verified (source + spec + chat observation). Not yet observed on `/embeddings` specifically. |
| Model name | `model` is the **deployment name**. | Verified (docs). |
| `dimensions` | Accepted for `text-embedding-3-*` (min 1); 1536 is the model's native output. Sending 1536 is expected to be a no-op. | Spec: verified. That Azure accepts `dimensions == native` without error: **inference**, to confirm in the live probe. |
| Batching | ≤ 2,048 inputs per request, ≤ 8,192 tokens per input, ≤ 300,000 tokens summed per request (400 above it). Repo batches 32. | Verified (docs). |
| Limits | Tier 0, `GlobalStandard`, `text-embedding-3-small`: **1,000 RPM (enforced per 10 s window), 1,000,000 TPM** — the subscription ceiling; the deployment gets whatever capacity it's created with. | Table verified. That this subscription is still Tier 0 comes from the chat ADR, not re-checked. |
| 429 shape | 429 with `retry-after-ms`, `x-ratelimit-*` headers; error body `{code, message, param?, type?, inner_error?}`. | Headers/schema verified from docs; exact body text not observed. |
| Quota breaker | Mechanically works (still catches `ClientResultException.Status == 429`), but its semantics are Gemini-specific. A burst would open it for 1,800 s and degrade query-time search too. Needs provider-shaped settings, not a rewrite. | Code reading + docs; behaviour inferred. |
| Versions | No bump. MCP host resolves `OpenAI` 2.13.0 / `System.ClientModel` 1.14.0 transitively; both already carry everything used. | Verified (`dotnet restore` assets). |

## 1. The embedder as it stands

- `api/Infrastructure/Embeddings/EmbeddingServiceCollectionExtensions.cs:27-33` — key =
  `GEMINI_API_KEY` env var if non-empty, else `cfg.ApiKey`; `new OpenAIClient(new ApiKeyCredential(apiKey),
  new OpenAIClientOptions { Endpoint = new Uri(cfg.Endpoint) })`;
  `.GetEmbeddingClient(cfg.EmbeddingModel).AsIEmbeddingGenerator()`. No transport, no policy, so
  ServiceDefaults' resilience handler does not attach (same as chat; ADR decision 7 says keep it that way).
- `EmbeddingOptions.cs:14` — bound from `Ai:Gemini`; defaults `Endpoint` = Google's compat URL,
  `EmbeddingModel` = `gemini-embedding-001`, `Dimensions` = 1536, `QuotaBreakerSeconds` = 1800
  (`:17-32`).
- `GeminiEmbedder.cs:55` — `new EmbeddingGenerationOptions { Dimensions = _dimensions }` on every call.
- `GeminiEmbedder.cs:72-98` — up to 4 attempts on `ClientResultException { Status: 429 }`, waiting
  20 s × attempt (20, 40, 60 s); after the 4th, open the breaker and throw
  `EmbeddingQuotaExceededException`.
- `GeminiEmbedder.cs:103-125` — while open (`QuotaBreakerSeconds`), every call throws immediately.
- Callers: `SearchIndexReconciler.cs:152-154` embeds in batches of `SearchIndex:EmbedBatchSize` (32,
  `api/Mcp/appsettings.json` / `SearchIndexOptions.cs:18`); `SemanticSearchService.cs:55` and `:145`,
  `ExemplarSearchService.cs:93` and `:159` embed queries/bullets. `ReconcileWorker.cs:54-61,84-86`
  backs off `SearchIndex:QuotaBackoffSeconds` (1800) on the typed exception.
- Registered only by the MCP host: `api/Mcp/Program.cs:57`.

## 2. Auth — bearer works; no `api-key` shim

**What the SDK sends (verified, source).** `OpenAIClient(ApiKeyCredential, OpenAIClientOptions)`
delegates to `OpenAIClientUtilities.CreateApiKeyAuthenticationPolicy`, which is
`ApiKeyAuthenticationPolicy.CreateHeaderApiKeyPolicy(credential, "Authorization", "Bearer")`, added as
a per-try policy
([OpenAIClient.cs @ 2.14.0](https://github.com/openai/openai-dotnet/blob/OpenAI_2.14.0/OpenAI/src/Custom/OpenAIClient.cs),
[OpenAIClientUtilities.cs @ 2.14.0](https://github.com/openai/openai-dotnet/blob/OpenAI_2.14.0/OpenAI/src/Utility/OpenAIClientUtilities.cs)).
So the key goes out as `Authorization: Bearer <key>`, not `api-key: <key>`.

**What the v1 endpoint accepts (verified, spec).** The v1 Embeddings REST reference lists three
security schemes, of which a request needs *any one*: `ApiKeyAuth` — *"Pass your API key in the
`api-key` header"*; `ApiKeyAuth_` — *"Pass your API key in the `authorization` header"*; and OAuth2
([REST reference — Embeddings, v1](https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/azureopenai/embeddings)).
The v1 guide's C# API-key sample is exactly the repo's construction —
`new OpenAIClient(new ApiKeyCredential("{your-api-key}"), new OpenAIClientOptions { Endpoint = new("https://YOUR-RESOURCE-NAME.openai.azure.com/openai/v1/") })`
([v1 API guide](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/api-version-lifecycle)).
Its REST sample uses `api-key`, which is where the "maybe it needs `api-key`" worry comes from; the
spec says both are accepted.

**What the chat path did (verified, repo).** The same question was open for chat and the live
probe closed it: *"`ApiKeyCredential` works. The SDK sends `Authorization: Bearer <key>`; the v1
endpoint accepts it"* (`manuals/adr-chat-provider-seam.md:160`). The shipped branch constructs the
identical client with no header shim
(`api/Agents/Configuration/ChatProviderServiceCollectionExtensions.cs:205-206, 220-225`), against
`https://experttojob-openai-swc.openai.azure.com/openai/v1/` (`api/Agents/appsettings.json:18`).

**Inference.** Auth is enforced per resource, not per operation, so Bearer on `/embeddings` on the
same resource with the same key should behave as it did on `/chat/completions`. Not observed on
`/embeddings`; the live probe should assert it.

## 3. Model name — the deployment name

Verified: *"The `model` value in each request is your Azure model deployment name. The examples use
`text-embedding-3-small`; replace it if your deployment has a different name"*
([Generate embeddings](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/embeddings)).
The troubleshooting line gives the failure: *"For a `404` response, confirm that the endpoint includes
`/openai/v1/` and that `model` contains a valid deployment name"* (same page).

So `EmbeddingModel` holds a deployment name on Azure, exactly as `Ai:AzureFoundry:Model` does for chat
(ADR decision 3, `manuals/adr-chat-provider-seam.md:80-83`). Two consequences:

- `GeminiEmbedder.Model` is logged (`GeminiEmbedder.cs:60-62`) and, if anything persists it as the
  embedding identity, it would record the **deployment** name. The response body carries the real
  model in `model` (`OpenAI.CreateEmbeddingResponse.model`, REST reference above), but M.E.AI's
  generator surfaces only `Usage` from the response
  ([OpenAIEmbeddingGenerator.cs @ release/10.10](https://github.com/dotnet/extensions/blob/release/10.10/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIEmbeddingGenerator.cs)).
  Whether the index should record provider/model identity (to detect a provider switch that needs a
  re-embed) is a question for the ADR, not answered here.
- A wrong deployment name fails at first call (404), not at startup — the same reason the chat
  guard demands a non-empty deployment name (`manuals/azure-openai-ichatclient-wiring.md:468-470`).

## 4. `dimensions` at 1536

**Verified (spec and docs):**

- Request field `dimensions`: *"The number of dimensions the resulting output embeddings should have.
  Only supported in `text-embedding-3` and later models. Constraints: min: 1"*
  ([REST reference — Embeddings, v1](https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/azureopenai/embeddings)).
- `text-embedding-3-small`: max request 8,192 tokens, **output dimensions 1,536**; *"The third
  generation embeddings models support reducing the size of the embedding via a new `dimensions`
  parameter"*
  ([Foundry Models sold by Azure — Embeddings](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure)).
- The client sends it: M.E.AI maps `result.Dimensions ??= options?.Dimensions ?? _dimensions`
  ([OpenAIEmbeddingGenerator.cs @ release/10.10](https://github.com/dotnet/extensions/blob/release/10.10/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIEmbeddingGenerator.cs));
  `OpenAI.Embeddings.EmbeddingGenerationOptions` has `int? Dimensions`
  ([generated model @ 2.14.0](https://github.com/openai/openai-dotnet/blob/OpenAI_2.14.0/OpenAI/src/Generated/Models/Embeddings/EmbeddingGenerationOptions.cs)).

**Inference, not verified:** a `dimensions` equal to the native size is a valid "reduction" to the
same length and returns the full 1,536-dim vector. No Microsoft page says so in terms, and none says
it is rejected. **Recommendation: keep sending it.** It is harmless if accepted, it keeps the
`vector(1536)` column contract explicit per request, and it is the one thing that stays correct if
someone later points the deployment at `text-embedding-3-large` (3,072 native) by mistake — the
request would still come back 1,536 rather than failing at insert. The EXP-52 live probe should
assert `Vector.Length == 1536` with `Dimensions = 1536` set, which settles the inference for a
fraction of a cent.

## 5. Batching limits

Verified, three independent limits
([Generate embeddings — best practices](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/embeddings),
[Quotas and limits reference](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/quotas-limits),
REST reference above):

| Limit | Value | Over it |
|---|---|---|
| Inputs per request (array length) | 2,048 | 400 |
| Tokens per input | 8,192 (all current embedding models) | 400 |
| Tokens summed across all inputs in one request | 300,000 | *"Requests above this limit fail with HTTP 400"* |
| Empty string input | not allowed (*"cannot be an empty string"*) | 400 |

Against the repo: `EmbedBatchSize` 32 is far inside 2,048. `ChunkProjection`
(`api/Application/Search/ChunkProjection.cs:25-50`) does **not** cap chunk length — a summary,
an experience narrative with its bullets, or a single bullet — so an 8,192-token single input is
theoretically reachable but not realistic for CV prose (~6,000 words). 32 × 8,192 = 262,144 is under
the 300,000 aggregate, so the current batch size cannot trip the aggregate limit even in the worst
case. Blank inputs are already filtered at the achievement level (`ChunkProjection.cs:31, 43`).
None of the three needs a code change.

Note that a 400 falls through `GeminiEmbedder`'s `when (ex.Status == 429)` filter and surfaces as a
generic failure: `ReconcileWorker` logs it and retries next tick (`ReconcileWorker.cs:62-67`), so one
oversize chunk would fail its whole batch forever. That is true on Gemini today too; noted, not new.

## 6. Rate limits, the 429, and the quota breaker

### 6.1 Limits for this subscription

Verified table, Tier 0 (the tier the chat ADR recorded for this subscription —
`manuals/adr-chat-provider-seam.md:201-203`; **not re-checked here**, and tiers auto-upgrade):

| Model | Deployment type | RPM | TPM |
|---|---|---|---|
| `text-embedding-3-small` | GlobalStandard | 1000 / 10s | 1,000,000 |
| `gpt-4.1-mini` (for comparison) | GlobalStandard | 200 | 200,000 |

([Quotas and limits — Tier 0](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/quotas-limits)).
Tier 0 lists no `DataZoneStandard` row for the embedding model, consistent with map constraint 4.
Tier 1 carries the same embedding numbers.

How it applies (verified,
[Manage quota](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/quota)): quota is a
subscription ceiling in TPM; *"When a deployment is created, the assigned TPM directly maps to the
tokens-per-minute rate limit enforced on its inferencing requests"*, with RPM set proportionally.
The deployment's limit is **the capacity it is created with**, not the tier maximum — a detail the
provisioning ticket must choose (the map's $1 ceiling is about spend, and capacity is not spend).
Enforcement: TPM against an *estimated* token count per request, reset each minute; RPM over *"a
small period of time, typically 1 or 10 seconds"*. The quotas page also says subscription-level
pooling across regions for `GlobalStandard` began after 2026-05-07.

**Inference (scale check).** A full re-embed of the 500-expert demo roster, at a guess of ~15–25
chunks per expert, is ~10k inputs / ~300 requests of 32 / well under 1M tokens. At 1,000 RPM that is
seconds of quota; the realistic binding constraint is the reconciler's own `IntervalSeconds` (30)
tick, not Azure. Compare the Gemini free tier's ~100 embeddings a day (map EXP-52 Notes).

### 6.2 The shape of a 429

Verified from docs, not observed:

- **Status** 429. **Headers:** `retry-after-ms` (*"Included in 429 responses. The recommended wait
  time (in milliseconds) before retrying"*) plus `x-ratelimit-limit-requests`,
  `x-ratelimit-limit-tokens`, `x-ratelimit-remaining-requests`, `x-ratelimit-remaining-tokens`,
  `x-ratelimit-reset-requests`, `x-ratelimit-reset-tokens` on every response
  ([Manage quota — rate limit response headers](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/quota)).
- **Body:** the v1 default error object `{ code, message, param?, type?, inner_error? }` (REST
  reference above). The quota page's message indicators distinguish four causes: *"Requests to … have
  been limited"* / *"Rate limit is exceeded"* (your TPM/RPM), *"The service is temporarily unable to
  process your request"* (capacity — transient), a *temporary rate-limit adjustment* visible as
  `x-ratelimit-limit-tokens` below the configured TPM (*"typically resolves within a few hours"*),
  and `max_tokens`-inflated estimates (chat only).
- **Unknown:** whether Azure also sends a standard `Retry-After` (seconds) header on 429. The docs
  name only `retry-after-ms`. This matters because of §6.3.

### 6.3 What the client stack does with one — and the breaker

**The SDK retries first (verified, source).** The OpenAI pipeline is built by
`ClientPipeline.Create` with no custom retry policy
([OpenAIClientUtilities.cs @ 2.14.0](https://github.com/openai/openai-dotnet/blob/OpenAI_2.14.0/OpenAI/src/Utility/OpenAIClientUtilities.cs)),
so the `System.ClientModel` default applies: 3 retries, delay `0.8 s × 2^(n-1)`, on 408/429/500/502/
503/504, using the server's wait **only if a `Retry-After` header is present and longer**
([ClientRetryPolicy.cs @ 1.14.0](https://github.com/Azure/azure-sdk-for-net/blob/System.ClientModel_1.14.0/sdk/core/System.ClientModel/src/Pipeline/ClientRetryPolicy.cs),
[PipelineResponseHeaders.cs @ 1.14.0](https://github.com/Azure/azure-sdk-for-net/blob/System.ClientModel_1.14.0/sdk/core/System.ClientModel/src/Message/PipelineResponseHeaders.cs),
[PipelineMessageClassifier.cs](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/core/System.ClientModel/src/Options/PipelineMessageClassifier.cs)).
`TryGetRetryAfter` reads `"Retry-After"` only — **not** `retry-after-ms`. (The Azure-specific
`AzureOpenAIClientOptions` retry policy does honour `retry-after-ms`,
`manuals/azure-openai-ichatclient-wiring.md:344-345`, but Route B deliberately doesn't use it.)

**Then `GeminiEmbedder` retries (verified, code).** A 429 that survives the SDK's 4 tries becomes one
`ClientResultException { Status: 429 }` (`GeminiEmbedder.cs:82`). The embedder waits 20/40/60 s and
tries again, each try again running the SDK's 4. Worst case before the breaker opens: **16 HTTP
requests over ~2.5 minutes**. Then the breaker opens for `QuotaBreakerSeconds` = 1,800 s.

**Does it behave sensibly on Azure? Partly — the mechanics yes, the policy no.**

- *Works unchanged:* the catch filter is provider-neutral (`ClientResultException.Status == 429`
  is what the same SDK raises for Azure), and a 20–60 s wait comfortably outlasts a 10-second RPM
  window or a one-minute TPM window. For an ordinary throttle the embedder's retry will usually
  succeed on attempt 2 and the breaker never opens.
- *Gemini-specific:* the breaker's premise, written into its own doc comments, is that a 429 which
  outlives the retry budget *"is the daily request cap, not a throttle"* (`GeminiEmbedder.cs:67-71`,
  `EmbeddingOptions.cs:29-31`, P1T-98/99). Azure has no daily cap on `GlobalStandard`; a 429 that
  outlives ~2.5 minutes is either sustained over-rate traffic or the documented capacity /
  temporary-adjustment cases, which resolve in minutes to hours. 1,800 s is a poor guess for any of
  them.
- *The blast radius is wider than the reconciler.* The `IEmbedder` is one singleton
  (`EmbeddingServiceCollectionExtensions.cs:36`), so a breaker opened by a reconciler burst also
  makes every query embed throw immediately; `SemanticSearchService` then silently serves **lexical
  fallback for 30 minutes** (`SemanticSearchService.cs:58-61`). On Gemini that trade was deliberate
  (the day's allowance was gone anyway). On Azure it would turn a seconds-long throttle into half an
  hour of degraded search.
- *`ReconcileWorker`'s own `QuotaBackoffSeconds` (1,800)* stacks on top, with the same daily-cap
  premise (`ReconcileWorker.cs:55-57`).

**Inference — what the build should do (for the ADR to decide, not decided here):** keep the
mechanism, make the numbers per-provider configuration. An Azure block would carry a
`QuotaBreakerSeconds` on the order of 60 s (and the reconciler a matching `QuotaBackoffSeconds`), so a
genuinely sustained 429 still fails fast without half an hour of lexical search. Honouring
`retry-after-ms` would need either a per-call pipeline policy (a shim, which the chat path has so far
avoided) or reading the header off `ClientResultException.GetRawResponse()` inside `GeminiEmbedder`;
at this roster's volume the fixed 20/40/60 s ladder is adequate and neither is needed. The
`EmbeddingQuotaExceededException` name and "daily cap" log text become misleading on Azure but not
wrong in behaviour.

## 7. Versions

Verified: **no package bump is needed.**

- `Directory.Packages.props:98-121` pins `Microsoft.Extensions.AI[.Abstractions|.OpenAI]` 10.10.0,
  `OpenAI` 2.14.0, `System.ClientModel` 1.16.0.
- But `api/Infrastructure` references only `Microsoft.Extensions.AI` and
  `Microsoft.Extensions.AI.OpenAI` (`api/Infrastructure/ExpertToJob.Infrastructure.csproj:13-14`), and
  transitive pinning is off (`Directory.Packages.props:36`). `dotnet restore` of `api/Mcp` on this
  branch resolves **`OpenAI` 2.13.0 and `System.ClientModel` 1.14.0** for both Infrastructure and the
  MCP host — the floors M.E.AI.OpenAI 10.10.0 declares — not the central 2.14.0 / 1.16.0 the Agents
  host gets.
- Everything used exists at those versions: `OpenAIClientOptions.Endpoint`, `ApiKeyCredential`,
  `GetEmbeddingClient`, `EmbeddingGenerationOptions.Dimensions`, `AsIEmbeddingGenerator`. The v1
  endpoint needs no `api-version` and no Azure package
  ([v1 API guide](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/api-version-lifecycle)).
- Optional hygiene, not required: add `<PackageReference Include="OpenAI" />` to Infrastructure so it
  resolves the same central 2.14.0 as the Agents host — the same "make an honest reference out of a
  transitive one" move the chat seam made (`Directory.Packages.props:113-121`). One line, no new
  `PackageVersion` entry.

## 8. What works unchanged vs what doesn't

| Piece | Unchanged? | Why |
|---|---|---|
| `new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint })` | **Yes** | Microsoft's own v1 C# sample, character for character; chat ships it. |
| `GetEmbeddingClient(x).AsIEmbeddingGenerator()` | **Yes**, with `x` = deployment name | §3. |
| `Dimensions = 1536` per request | **Yes** (keep it) | §4; one live assertion to settle the inference. |
| Batch size 32, chunk sizes | **Yes** | §5. |
| 429 catch + retry ladder | **Yes**, mechanically | §6.3. |
| Package versions | **Yes** | §7. |
| Key lookup: `GEMINI_API_KEY` env var first (`EmbeddingServiceCollectionExtensions.cs:27`) | **No** | If `GEMINI_API_KEY` is exported (the MCP host reads it from the inherited environment, `api/AppHost/Program.cs:81-82`, and CLAUDE.md tells developers to keep it for embeddings) it wins over any configured Azure key: a 401 from Azure, and a Google credential sent to Microsoft. Needs the chat seam's pattern — the *active provider's* env var (`AZURE_FOUNDRY_API_KEY`, `ChatProviderServiceCollectionExtensions.cs:220-222`). |
| Config section `Ai:Gemini` for `Endpoint`/`EmbeddingModel`/`ApiKey` (`EmbeddingOptions.cs:14`) | **No** | Pointing `Ai:Gemini:Endpoint` at Azure would work mechanically but make the config lie; the seam needs an Azure block (map constraint 5: same resource and key as chat, i.e. `Ai:AzureFoundry`). |
| Endpoint/model defaults in `EmbeddingOptions` | **No** | Google URL and `gemini-embedding-001`. |
| Breaker/backoff windows (1,800 s) and their daily-cap premise | **No** (semantics) | §6.3 — per-provider values. |
| Names `GeminiEmbedder`, `AddGeminiEmbeddings` | Cosmetic | Nothing behavioural. |

## 9. Left open

- **`dimensions == native` accepted by Azure** — inferred, not documented. One live call settles it.
- **Bearer on `/embeddings`** — observed for chat on the same resource; not for embeddings.
- **Whether a 429 also carries `Retry-After`** — docs name only `retry-after-ms`; decides whether the
  SDK's own retries wait the server's time or just 0.8/1.6/3.2 s.
- **The subscription's current quota tier** — Tier 0 per the chat ADR (2026-09-20); tiers
  auto-upgrade and weren't re-checked (reading it needs a management-plane call, not made here).
- **The deployment's capacity** — set at provisioning; the table above is only its ceiling.
- **Recording provider/model identity with each vector** — needed to detect a re-embed-requiring
  switch; an ADR question.

## Sources

Fetched 2026-09-27.

- Microsoft Learn — [Generate embeddings with Azure OpenAI](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/embeddings) (2026-07-22) · [REST reference — Embeddings (v1)](https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/azureopenai/embeddings) (2026-05-27) · [Azure OpenAI v1 API guide](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/api-version-lifecycle) (2026-05-13) · [Quotas and limits](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/quotas-limits) (2026-08-20) · [Manage Azure OpenAI quota](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/quota) (2026-05-04, classic) · [Foundry Models sold by Azure](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure) (2026-09-21)
- openai/openai-dotnet @ `OpenAI_2.14.0` — [OpenAIClient.cs](https://github.com/openai/openai-dotnet/blob/OpenAI_2.14.0/OpenAI/src/Custom/OpenAIClient.cs) · [OpenAIClientUtilities.cs](https://github.com/openai/openai-dotnet/blob/OpenAI_2.14.0/OpenAI/src/Utility/OpenAIClientUtilities.cs) · [EmbeddingGenerationOptions.cs (generated)](https://github.com/openai/openai-dotnet/blob/OpenAI_2.14.0/OpenAI/src/Generated/Models/Embeddings/EmbeddingGenerationOptions.cs). (The MCP host resolves 2.13.0; the auth and pipeline code read here is the one-minor-newer tag.)
- dotnet/extensions @ `release/10.10` — [OpenAIEmbeddingGenerator.cs](https://github.com/dotnet/extensions/blob/release/10.10/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIEmbeddingGenerator.cs)
- Azure/azure-sdk-for-net — `System.ClientModel_1.14.0`: [ClientRetryPolicy.cs](https://github.com/Azure/azure-sdk-for-net/blob/System.ClientModel_1.14.0/sdk/core/System.ClientModel/src/Pipeline/ClientRetryPolicy.cs) · [PipelineResponseHeaders.cs](https://github.com/Azure/azure-sdk-for-net/blob/System.ClientModel_1.14.0/sdk/core/System.ClientModel/src/Message/PipelineResponseHeaders.cs); `main`: [PipelineMessageClassifier.cs](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/core/System.ClientModel/src/Options/PipelineMessageClassifier.cs)
- This repo — `api/Infrastructure/Embeddings/*`, `api/Infrastructure/Search/{SearchIndexReconciler,SemanticSearchService,SearchIndexOptions}.cs`, `api/Mcp/Search/ReconcileWorker.cs`, `api/Mcp/appsettings.json`, `api/Application/Search/ChunkProjection.cs`, `api/Agents/Configuration/ChatProviderServiceCollectionExtensions.cs`, `api/Agents/appsettings.json`, `api/AppHost/Program.cs`, `Directory.Packages.props`, `manuals/adr-chat-provider-seam.md`, `manuals/azure-openai-ichatclient-wiring.md`
