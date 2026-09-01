# Cloudflare Workers AI as a second free chat provider (research)

Verified against live Cloudflare docs on 2026-08-30.

**Headline: the plumbing is a half-day — Workers AI speaks the same OpenAI-compatible dialect our
`IChatClient` wiring already targets, and its free tier (10,000 neurons/day) is ~20x our own
`Usage:DefaultDailyTokens` cap. But the three LoRA models we were asked about
(`gemma-2b-it-lora`, `mistral-7b-instruct-v0.2-lora`, `llama-2-7b-chat-hf-lora`) support neither
function calling nor JSON-schema `response_format`, which is exactly what all eight of our agents
need. The usable free model is `@cf/meta/llama-3.1-8b-instruct-fast`.**

## 1. What Workers AI offers

| Fact | Value | Source |
|---|---|---|
| Free allocation | 10,000 Neurons/day, on **both** Workers Free and Workers Paid | [pricing](https://developers.cloudflare.com/workers-ai/platform/pricing/) |
| Overage | $0.011 per 1,000 Neurons | pricing |
| OpenAI-compatible base URL | `https://api.cloudflare.com/client/v4/accounts/{account_id}/ai/v1` | [openai-compatibility](https://developers.cloudflare.com/workers-ai/configuration/open-ai-compatibility/) |
| Compatible endpoints | `/v1/chat/completions`, `/v1/embeddings` | openai-compatibility |
| Auth | `Authorization: Bearer <Cloudflare API token>`; account id lives in the URL, not a header | openai-compatibility |

### Neurons → tokens, in our terms

Neuron cost is per-model. The nearest priced 7B row, `@cf/mistral/mistral-7b-instruct-v0.1`, costs
**10,000 neurons/M input tokens** and **17,300 neurons/M output tokens**. So the daily free grant
buys roughly:

* ~1,000,000 input tokens/day, **or**
* ~578,000 output tokens/day

Our own per-user meter (`api/Agents/appsettings.json` → `Usage:DefaultDailyTokens: 50000`) is an
order of magnitude below that. Unlike the Gemini free tier, the binding constraint would not be
requests-per-day — see `manuals/`-adjacent P1T-114/P1T-115 notes on the Flash RPD-20 trap.

The LoRA rows are **not in the pricing table at all**: the fine-tunes feature is "in open beta and
free during this period".

## 2. Why the three requested models don't fit

| Model | Ctx | Function calling | JSON mode | Status |
|---|---|---|---|---|
| `@cf/google/gemma-2b-it-lora` | 8,192 | No | No | Beta |
| `@cf/mistral/mistral-7b-instruct-v0.2-lora` | 15,000 | No | No | Beta |
| `@cf/meta-llama/llama-2-7b-chat-hf-lora` | 8,192 | No | No | Beta on the model page, **Deprecated** in the catalog's LoRA filter — conflicting, confirm before use |

Cloudflare's [JSON mode](https://developers.cloudflare.com/workers-ai/features/json-mode/)
model list is llama-3.x / `hermes-2-pro-mistral-7b` / `deepseek-coder` / `deepseek-r1-distill`
only. None of the three appear on it, nor on the function-calling capability filter.

### What that breaks in this repo

Tool-calling agents — dead without function calling:

* `api/Agents/Agents/RosterQaAgent.cs:97` — `ChatOptions { ToolMode = ChatToolMode.RequireAny }`
* `api/Agents/Agents/MatchAgent.cs:70`
* `api/Agents/Agents/ResumeIngestionAgent.cs:131`

Schema-constrained agents — dead without JSON mode (all use `ChatResponseFormat.ForJsonSchema`):

* `api/Agents/RosterScan/ScoringTransport.cs:117` (`roster_scan_chunk`)
* `api/Agents/Agents/ShortlistAgent.cs:89` (`shortlist_rationales`)
* `api/Agents/Agents/JdRequirementExtractor.cs:64` (`GetResponseAsync<JdRequirements>` with
  `useJsonSchemaResponseFormat: true`)
* `api/Agents/Staffing/StaffingPipeline.cs:613` (`staffing_narrative`) — the **only** one with a
  `TryParse` fallback for prompt-only JSON, so the only one that would degrade rather than fail

Context ceilings bite too: roster-scan sends `RosterScanOptions.ChunkSize = 10` digests plus the
JD plus the schema per call (`api/Agents/RosterScan/ScoringTransport.cs:49`); 8,192 tokens is tight.

Finally, the LoRA point is moot: those ids are *base models Cloudflare dedicates to adapter
inference*. With no adapter attached they are raw 2023-era 2B/7B checkpoints. Training an adapter
needs a labeled dataset we do not have.

### LoRA beta terms, if we ever do train adapters

Free during open beta · adapter file < 300MB · rank r ≤ 8 (up to 32 supported) · files must be
named `adapter_model.safetensors` + `adapter_config.json` · `model_type` ∈ `mistral` | `gemma` |
`llama` · up to 100 adapters per account · assets are immutable after upload (re-create to change).
Source: [fine-tunes/loras](https://developers.cloudflare.com/workers-ai/features/fine-tunes/loras/).

## 3. The model that does fit

`@cf/meta/llama-3.1-8b-instruct-fast` — function calling **and** JSON mode **and** a 128,000-token
context, and it is itself LoRA-capable if we later want adapters. Same free neuron pool. This is
the drop-in candidate for a Gemini-429 fallback provider.

## 4. What the code change looks like

The seam is already provider-agnostic — every agent takes `IChatClient`, and the Gemini backend is
just an OpenAI-compatible endpoint + key + model id
(`api/Agents/Configuration/GeminiServiceCollectionExtensions.cs`).

What blocks a second provider today: there is exactly **one** `OpenAIClient` singleton, and a
per-agent override changes the *model id only*
(`api/Agents/Configuration/AgentsOptions.cs` → `GeminiOptions.Agents`). Cloudflare needs its own
endpoint (account-id in the path) and its own key, so the config shape has to widen from
`agent → model` to `agent → { endpoint, apiKey, model }`, with the current Gemini block as the
default profile. `MeteringChatClient` and the OpenTelemetry wrapper are per-client and unaffected.

## 5. Embeddings: separate ticket, do not bundle

Workers AI exposes `/v1/embeddings` (bge family) on the same free pool, but no bge model emits
1536 dimensions, and `ExpertSearchChunk` pins `vector(1536)`
(`api/Infrastructure/Embeddings/EmbeddingOptions.cs` → `Dimensions = 1536`). Swapping the embedder
means a schema change plus a full reindex. Out of scope for the chat-provider work.

## 6. Recommended slice

1. **Gate prototype.** One throwaway script hitting the Cloudflare compat endpoint with
   `ScoringTransport`'s exact `roster_scan_chunk` schema over ~10 real digest chunks, on
   `@cf/meta/llama-3.1-8b-instruct-fast`. Measure: schema adherence rate, neuron burn, latency.
2. **Gate.** If schema adherence holds, widen the provider config to per-agent
   `{ endpoint, apiKey, model }` and wire Cloudflare as the fallback profile behind the existing
   quota breaker. If it does not hold, stop — record the numbers here and close.
