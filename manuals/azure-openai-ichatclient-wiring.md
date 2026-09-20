# Azure OpenAI → `IChatClient`: how the wiring differs from the Gemini path (research, P1T-244)

Research for P1T-241 (*a second chat provider — Azure AI Foundry, selected by configuration*). The
question is narrow and mechanical: what does it actually take to hand `api/Agents` an `IChatClient`
that talks to an Azure OpenAI deployment, and how much of the existing Gemini wiring survives the
move. Verified against live docs on 2026-09-20, plus two compile-and-run probes against the real
packages (below). No Azure resource was created, read or touched — everything here is package
surface and documentation.

**Headline: the Azure path is the *same* path. `AzureOpenAIClient` derives from `OpenAIClient`,
`GetChatClient(deployment).AsIChatClient()` is the identical call, and both routes end in the same
`OpenAIChatClient` adapter. Neither Gemini hack has an analog — none known, and the structural
reason is that both are Gemini-endpoint bugs, not OpenAI-protocol gaps. The live question is not
"which client class" but "which of two Azure routes": `Azure.AI.OpenAI` (prerelease-only since
2.1.0, dated `api-version`, Azure SDK's own retry policy) versus the plain `OpenAI` package against
the v1 endpoint (`…/openai/v1/`) — which is what Microsoft's own .NET docs now show, what its
migration guide tells you to move to, and what this repo already has on disk for Gemini.**

## 0. What was verified, and how

| Evidence | What it proves |
|---|---|
| Compile+run probe A (net10.0, `Azure.AI.OpenAI` 2.9.0-beta.1 + `Microsoft.Extensions.AI[.OpenAI]` 10.9.0) | All three constructions compile and run: `AzureOpenAIClient(Uri, ApiKeyCredential)`, the same with `AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient) }`, and a plain `OpenAIClient` against `https://…/openai/v1/`. NuGet resolved `OpenAI` **2.12.0**; all three `.AsIChatClient()` results are the same type, `OpenAIChatClient`. `ChatResponseFormat.ForJsonSchema(...)` binds. |
| Compile+run probe B (same, but `Azure.AI.OpenAI` **2.1.0**) | 2.1.0 also loads and constructs against `OpenAI` 2.12.0 — no `MissingMethodException` at construction. Not proof of wire behaviour; no request was sent. |
| Live docs, listed in [§10](#10-sources) | Everything else. |

Neither probe called a service. Nothing in this note is evidence about a real deployment's
behaviour — that is what P1T-241's live smoke test is for.

## 1. The Gemini path as it stands today

`api/Agents/Configuration/GeminiServiceCollectionExtensions.cs`:

1. one singleton `OpenAIClient` = `new OpenAIClient(new ApiKeyCredential(key), options)` where
   `options.Endpoint = https://generativelanguage.googleapis.com/v1beta/openai`;
2. `options.Transport = new HttpClientPipelineTransport(new HttpClient(new GeminiCompatHandler(new HttpClientHandler())))`
   — a **response-side** shim: retry on `MALFORMED_FUNCTION_CALL`, rewrite unknown `finish_reason`
   values to `"stop"` so the OpenAI SDK's enum parser does not throw;
3. `options.AddPolicy(new GeminiThoughtSignaturePolicy(), PipelinePosition.PerCall)` — a
   **request-side** shim injecting Google's `skip_thought_signature_validator` sentinel into every
   replayed assistant `tool_calls[n].extra_content.google.thought_signature`, without which Gemini 3
   answers 400 INVALID_ARGUMENT;
4. `.GetChatClient(cfg.Model).AsIChatClient()` for the default client, the same per agent for each
   `Gemini:Agents:<agent>` override, each wrapped in `UseOpenTelemetry` + `MeteringChatClient`, and
   at resolve time in `RuntimeBudgetChatClient`.

Two facts about that pipeline matter for the comparison: the `HttpClient` is hand-built, so
`ServiceDefaults`' resilience handler never sees it (it says so, in a comment, in
`api/ServiceDefaults/Extensions.cs`); and the model id is a *model* id — `gemini-3.5-flash-lite` —
pinned to a generation for quota reasons.

## 2. Packages: what is current, and what would land in `Directory.Packages.props`

### 2.1 `Azure.AI.OpenAI`

* Latest **stable**: **2.1.0** (Dec 2024). Latest overall: **2.9.0-beta.1** (2026-03-13). Every
  release since 2.1.0 is a beta — 2.2.0-beta.1 … 2.9.0-beta.1
  ([NuGet flat container index](https://api.nuget.org/v3-flatcontainer/azure.ai.openai/index.json)).
  The package README still tells you to install it with `--prerelease`
  ([README](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.openai-readme)).
* It "builds on the official OpenAI package, which is included as a dependency" (same README). The
  nuspec dependency is a **floor, not a range**: 2.9.0-beta.1 declares `OpenAI >= 2.9.1` and
  `Azure.Core >= 1.51.1`; 2.1.0 declares `OpenAI >= 2.1.0`, `Azure.Core >= 1.44.1`
  ([2.9.0-beta.1 nuspec](https://api.nuget.org/v3-flatcontainer/azure.ai.openai/2.9.0-beta.1/azure.ai.openai.nuspec),
  [2.1.0 nuspec](https://api.nuget.org/v3-flatcontainer/azure.ai.openai/2.1.0/azure.ai.openai.nuspec)).
* The release notes for 2.9.0-beta.1 carry a Microsoft note recommending you **remove the Azure
  OpenAI SDK from your application** in favour of the OpenAI SDK, with a migration guide
  ([CHANGELOG](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/openai/Azure.AI.OpenAI/CHANGELOG.md),
  [migration-guidance.md](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/openai/Azure.AI.OpenAI/migration-guidance.md)).

### 2.2 The constraint on `OpenAI`

There is no `OpenAI` entry in `Directory.Packages.props` today, and no csproj references it:
`api/Agents` gets `OpenAIClient` **transitively**, through `Microsoft.Extensions.AI.OpenAI`. That
package is the one holding the tight pin —

```
Microsoft.Extensions.AI.OpenAI 10.9.0  →  OpenAI [2.12.0, 2.13.0)
Microsoft.Extensions.AI.OpenAI 10.10.0 →  OpenAI 2.13.0
Microsoft.Agents.AI.OpenAI     1.10.0  →  Microsoft.Extensions.AI.OpenAI 10.6.0, OpenAI 2.10.0
```

([10.9.0 nuspec](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai.openai/10.9.0/microsoft.extensions.ai.openai.nuspec),
[10.10.0](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai.openai/10.10.0/microsoft.extensions.ai.openai.nuspec),
[Microsoft.Agents.AI.OpenAI 1.10.0](https://api.nuget.org/v3-flatcontainer/microsoft.agents.ai.openai/1.10.0/microsoft.agents.ai.openai.nuspec).)

So the **bracketed range wins**: with M.E.AI at 10.9.0, `OpenAI` resolves to 2.12.0 whatever
`Azure.AI.OpenAI` asks for, because its ask is a floor below 2.12.0. Probe A confirmed exactly that
(`Azure.AI.OpenAI 2.9.0.0` loaded next to `OpenAI 2.12.0.0`). No version conflict, no MSB3277, and
nothing forced on the existing M.E.AI pins. It does mean `Azure.AI.OpenAI` runs against an `OpenAI`
five minors newer than the one it was built and tested against, which is a risk the package's own
release cadence ("this update restores compatibility with the latest 2.9.\* release of `OpenAI`")
says out loud: the Azure package tracks the OpenAI package release by release, and is a beta each
time it catches up.

### 2.3 What has to land in `Directory.Packages.props`

**Route B — plain `OpenAI` against the v1 endpoint (recommended): nothing at all.** The Azure
client is `OpenAIClient`, already on disk transitively, already the type the Gemini registration
uses. If the repo wants the reference to be honest rather than transitive — reasonable, since
`GeminiServiceCollectionExtensions.cs` has `using OpenAI;` and names `OpenAIClient` directly — then
one entry and one `PackageReference` in `api/Agents`, pinned inside M.E.AI's range:

```xml
<PackageVersion Include="OpenAI" Version="2.12.0" />
```

A pin *outside* `[2.12.0, 2.13.0)` is a restore error while M.E.AI is 10.9.0, so this entry ties
itself to the M.E.AI line and wants a comment saying so.

**Route A — `Azure.AI.OpenAI`:** one new entry, plus a second if `Azure.Core` is ever named
directly (it is not today, and does not need to be — it comes transitively):

```xml
<PackageVersion Include="Azure.AI.OpenAI" Version="2.9.0-beta.1" />
```

That is a **prerelease** version in a repo whose one stated package rule of this kind is
"no prerelease Aspire package enters this solution" (`manuals/adr-aspire-apphost.md`). Pinning the
stable 2.1.0 instead buys a GA label and a two-year-old surface whose newest `api-version` is
`2024-10-21`; see §6.2. Entra later (`DefaultAzureCredential`) adds `Azure.Identity` on either
route — Route B needs it too, because the v1 Entra sample is `BearerTokenPolicy` +
`DefaultAzureCredential` from `Azure.Identity`.

## 3. Construction with an API key, and the endpoint shape

**Route A**, exactly as the README shows it
([README](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.openai-readme)):

```csharp
AzureOpenAIClient azureClient = new(
    new Uri("https://your-azure-openai-resource.com"),
    new ApiKeyCredential(keyFromEnvironment));
ChatClient chatClient = azureClient.GetChatClient("my-gpt-35-turbo-deployment");
```

The ctor XML doc pins the endpoint contract: *"The Azure OpenAI resource endpoint to use. This
should not include model deployment or operation information. For example:
`https://my-resource.openai.azure.com`"*
([AzureOpenAIClient.cs](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.9.0-beta.1/sdk/openai/Azure.AI.OpenAI/src/Custom/AzureOpenAIClient.cs)).
Note what it is *not*: there is no `Endpoint` property on `AzureOpenAIClientOptions`, because
`AzureOpenAIClientOptions : ClientPipelineOptions`, not `: OpenAIClientOptions`. The endpoint is a
constructor argument. That is a real shape difference from the Gemini registration, which sets
`options.Endpoint`.

**Route B**, the v1 API, from Microsoft's own C# sample
([api-version-lifecycle](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/api-version-lifecycle)):

```csharp
OpenAIClient client = new(
    new ApiKeyCredential("{your-api-key}"),
    new OpenAIClientOptions()
    {
        Endpoint = new("https://YOUR-RESOURCE-NAME.openai.azure.com/openai/v1/"),
    })
```

That is, character for character, the shape already in `GeminiServiceCollectionExtensions.cs`.

### Endpoint URLs a Foundry / AI Services resource exposes

| Form | Where it is used | Source |
|---|---|---|
| `https://<resource>.openai.azure.com` | resource root — Route A ctor argument | [README](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.openai-readme) |
| `https://<resource>.cognitiveservices.azure.com/?api-version=<version>` | described as the Azure-specific routing the `Azure.AI.OpenAI` client uses | [migration-guidance.md](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/openai/Azure.AI.OpenAI/migration-guidance.md) |
| `https://<resource>.openai.azure.com/openai/v1/` | Route B base URL | [api-version-lifecycle](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/api-version-lifecycle) |
| `https://<resource>.services.ai.azure.com/openai/v1/` | Route B base URL for a Foundry / multi-service (AI Services) resource — *"`base_url` accepts both … formats"* | same |

The prerequisites for the v1 API name either *"a Foundry resource … or Azure OpenAI resource"* plus
*"at least one model deployment"*, so a Foundry resource is in scope for exactly this wiring (same
page). Which hostname a real EU Foundry resource prints is a fact to capture at provisioning time,
not from docs — the map already has provisioning as a blocking task.

## 4. Deployment name vs model id

Confirmed, and it is the deployment name on both routes.

* Route A: `GetChatClient(string deploymentName)` — the parameter is literally named
  `deploymentName`, the README passes `"my-gpt-4o-mini-deployment"`, and the samples add *"you
  should use an Azure OpenAI model deployment name wherever a model name is requested"*
  ([README](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.openai-readme),
  [AzureOpenAIClient.cs](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.9.0-beta.1/sdk/openai/Azure.AI.OpenAI/src/Custom/AzureOpenAIClient.cs)).
* Route B: *"The `model` value in every request is your Azure model deployment name"*
  ([supported-languages](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/supported-languages)),
  and the migration table says the same: *"Model/deployment … Required as `model` (your deployment
  name)"*. The 404 troubleshooting entry on that page — *"confirm that the base URL ends in
  `/openai/v1/` and that `model` contains a valid deployment name"* — is the failure mode for
  getting this wrong.

**What that means for a config key named `Model`.** Under `Gemini:Model` the value is a catalogue
identifier the provider publishes, and pinning it is a quota decision. Under an Azure provider the
same key is an **operator-chosen name** for a deployment in a specific resource, which happens to
default to the model name in the portal but need not equal it; the same physical model in two
resources can be two different strings, and the string means nothing outside its resource. The seam
should either keep the key spelled `Model` and document in the options XML doc that on Azure it is
the *deployment* name (cheap; per-agent overrides under `Ai:Agents:<agent>` keep working unchanged),
or spell it `Deployment` per provider and accept two shapes. Only the first keeps "two factories,
one contract" literally true for the per-agent override dictionary.

Also worth writing into the ticket: the deployment name is what lands in usage rows and OTel
`gen_ai.request.model` today via `MeteringChatClient`. On Azure that stops being a model identity —
P1T-241's note 11 ("usage rows record provider identity") should say whether the real model id
(returned as `model` in the response body, e.g. `gpt-4o-2024-08-06`) is captured alongside it.

## 5. `.AsIChatClient()`

One extension, on `OpenAI.Chat.ChatClient`:

```csharp
public static IChatClient AsIChatClient(this ChatClient chatClient) => …
```

([OpenAIClientExtensions.cs, release/10.9](https://github.com/dotnet/extensions/blob/release/10.9/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs)).
There is no Azure-specific overload and none is needed: `AzureChatClient : ChatClient`
([AzureChatClient.cs](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.9.0-beta.1/sdk/openai/Azure.AI.OpenAI/src/Custom/Chat/AzureChatClient.cs)),
so the Azure client flows into the same extension and yields the same concrete
`OpenAIChatClient` — probe A printed `OpenAIChatClient` for all three constructions.

Required `Microsoft.Extensions.AI` version: **the one already pinned, 10.9.0**. Nothing about the
Azure path raises the floor. (10.9.0 is itself a floor this repo must not drop below, for the
schema-generation race recorded in `Directory.Packages.props` / P1T-223.)

The deeper point for the seam: because `AzureOpenAIClient : OpenAIClient`
([AzureOpenAIClient.cs](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.9.0-beta.1/sdk/openai/Azure.AI.OpenAI/src/Custom/AzureOpenAIClient.cs)),
today's registration — `services.AddSingleton(_ => new OpenAIClient(…))` resolved as
`sp.GetRequiredService<OpenAIClient>().GetChatClient(model)` — would accept an `AzureOpenAIClient`
**with no change to the resolution code at all**. The provider switch is a factory over the
singleton, not a second seam.

## 6. Does the Azure path need an analog of either Gemini hack?

### 6.1 Thought signatures — **none known**

`GeminiThoughtSignaturePolicy` exists because Gemini 3 signs each function call and rejects the next
turn if `tool_calls[n].extra_content.google.thought_signature` is missing. There is no such field,
requirement or error in the OpenAI Chat Completions protocol that Azure serves, and no Azure doc
names one. The nearest relative — round-tripping *reasoning* items — is a **Responses API** concern
(the v1 preview changelog lists "Encrypted reasoning items" as a Responses feature,
[api-version-lifecycle](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/api-version-lifecycle)),
and this repo reaches chat through `ChatClient.AsIChatClient()`, i.e. `/chat/completions`, not
`/responses`. **Verdict: no analog needed. If the seam ever moves to the Responses API
(`ResponsesClient.AsIChatClient()`), re-open this question — that is where reasoning-item echo
lives.**

### 6.2 Response-shape shim — **none known**, and the shape differences that do exist are handled elsewhere

`GeminiCompatHandler` exists because Gemini returns `finish_reason` values outside the OpenAI enum.
Azure returns OpenAI's own values plus `content_filter`, which is already a known value in both the
OpenAI SDK's enum and the repo's own `KnownFinishReasons` list. The Azure-specific response
behaviour that *does* exist is documented, not malformed:

* a filtered **prompt** fails the call with **HTTP 400**, body
  `{"error":{"message":"The response was filtered","code":"content_filter","status":400}}`;
* a filtered **completion** returns 200 with `finish_reason: "content_filter"` and (possibly) no
  content — *"Always check the `finish_reason`"*;
* responses carry `content_filter_results` / annotation objects, which can themselves carry an
  `error` when the filter did not run
  ([content filtering](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/concepts/content-filter)).

None of that needs a `DelegatingHandler`; it needs the orchestration to degrade a stage the way it
already degrades a failed one. It *is* a behaviour the Gemini path has never met, so a build ticket
should name it.

Two further quirks exist and are **already inside** `Azure.AI.OpenAI` on Route A, not something we
would write: `RefreshMaxTokenSerialization` (Azure accepts `max_tokens` for most models and
`max_completion_tokens` for the o-series) and `PostfixClearStreamOptions` (Azure rejects
`stream_options` when using On-Your-Data or image parts)
([AzureChatClient.cs](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.9.0-beta.1/sdk/openai/Azure.AI.OpenAI/src/Custom/Chat/AzureChatClient.cs)).
That is the honest argument *for* Route A — and the argument against it is in the same file: the
client stamps a dated `api-version` on every request, and the newest one 2.9.0-beta.1 knows is
`2025-04-01-preview`; the newest the GA build knows is `2024-10-21`
([AzureOpenAIClientOptions.cs](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.9.0-beta.1/sdk/openai/Azure.AI.OpenAI/src/Custom/AzureOpenAIClientOptions.cs)).
Route B's whole point is that *"`api-version` is no longer a required parameter with the v1 GA
API"*. Where Route A's shims matter (max-token spelling) the v1 API is the thing that made them
unnecessary.

**So: zero Gemini-style hacks on either Azure route, and Route B carries no client-side shim at
all.** The nearest thing to "a hack this repo would have to write" is on the *audio* path, which is
out of scope — the migration guide documents an `ApiVersionPipelinePolicy` workaround for audio
transcription routing on v1 endpoints, and nothing for chat.

## 7. Structured output

The M.E.AI knob is provider-neutral and already present; what changes is whether the service honours
it. `Microsoft.Extensions.AI.OpenAI` maps `ChatOptions.ResponseFormat`:

```csharp
ChatResponseFormatJson jsonFormat when StrictSchemaTransformCache.GetOrCreateTransformedSchema(jsonFormat) is { } jsonSchema =>
    OpenAI.Chat.ChatResponseFormat.CreateJsonSchemaFormat(…, HasStrict(options?.AdditionalProperties)),
ChatResponseFormatJson => OpenAI.Chat.ChatResponseFormat.CreateJsonObjectFormat(),
```

— i.e. `ChatResponseFormat.ForJsonSchema(...)` becomes `json_schema`, and **`strict: true` is opt-in
via `ChatOptions.AdditionalProperties["strict"] = true`**; the same key on an `AIFunction` turns on
strict tool calling
([OpenAIChatClient.cs](https://github.com/dotnet/extensions/blob/release/10.9/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIChatClient.cs),
[OpenAIClientExtensions.cs](https://github.com/dotnet/extensions/blob/release/10.9/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs)).
M.E.AI's `StrictSchemaTransformCache` already strips the keywords Azure rejects, citing the Azure
doc by URL in its source — so the schema our `AIFunction`s generate is pre-adapted for Azure.

Azure's side is documented and GA: structured outputs are supported on Chat Completions
(`response_format: {type: json_schema, …, strict: true}`) and on Responses, function calling with
`strict: true` is supported, `parallel_tool_calls` must be `false` when combined with it, all fields
must be `required`, `additionalProperties:false` everywhere, ≤100 properties and ≤5 levels of
nesting, `$defs` and recursion supported, a keyword denylist per type, and *"API version
`2024-08-01-preview` is the first version that supports structured outputs. The latest preview APIs
and the latest GA API, `v1`, also support structured outputs"*. Supported models are enumerated
(gpt-5 family, gpt-4.1 family, gpt-4o ≥ 2024-08-06, o-series …)
([structured outputs](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/structured-outputs)).

Against that, `manuals/anthropic-gemini-ichatclient-mapping.md` records where the repo stands:
neither `ChatResponseFormat` nor `ChatToolMode` is set anywhere today; every agent relies on
prompt-based JSON and lenient parsers. So the answer to "does Azure make something available that
Gemini does not" is: **Azure makes grammar-strict, model-list-documented, GA structured output
available on the same call we already can make** — a tier the Gemini free-tier path documents more
weakly. Nothing here says to change what the agents do; it says an Azure provider unlocks a knob
that is worth its own ticket, and that a build ticket for the seam must not quietly set it on one
provider only (that would make the two providers behave differently under one contract).

## 8. `IHttpClientFactory` and the ServiceDefaults resilience handler

**Yes — on both routes, and this is the one place the Azure path can be made *better* than the
Gemini one.** `AddServiceDefaults` calls
`builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler())`, which
by construction only reaches clients built by `IHttpClientFactory`. Both client option types expose
`Transport`, and `HttpClientPipelineTransport` takes an `HttpClient`:

* `OpenAIClientOptions : ClientPipelineOptions` (already used this way for Gemini);
* `AzureOpenAIClientOptions : ClientPipelineOptions`
  ([AzureOpenAIClientOptions.cs](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.9.0-beta.1/sdk/openai/Azure.AI.OpenAI/src/Custom/AzureOpenAIClientOptions.cs)) —
  probe A compiled and ran `new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(httpClient) }`.

So `sp.GetRequiredService<IHttpClientFactory>().CreateClient("azure-openai")` → transport → client
gives the Azure client the standard handler, whereas Gemini's `new HttpClient(...)` cannot have it.

**But take the defaults deliberately, not by accident.** `AddStandardResilienceHandler` ships: total
request timeout **30 s**, **3** retries with exponential backoff and jitter (2 s base), a circuit
breaker (10 % failure ratio, 100 min throughput, 30 s sampling, 5 s break), and a per-attempt
timeout of **10 s**
([HTTP resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)). A
tool-calling completion routinely exceeds 10 s, so the attempt timeout would cancel healthy calls,
and the retries stack on top of the SDK's own: the OpenAI pipeline already retries 408/429/500/502/
503/504 with backoff ([supported-languages](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/supported-languages)),
and `AzureOpenAIClientOptions` installs a `RetryWithDelaysPolicy` that additionally honours
`retry-after-ms` / `x-ms-retry-after-ms` / `Retry-After`. Attaching the standard handler unchanged
means retry-of-a-retry against a metered endpoint. The useful shape is a *named* client with
explicit options (long attempt timeout, retries disabled or reduced, breaker kept), which is a
decision for the build ticket — and a reason to say out loud whether the model call should be
resilient-by-handler at all, given the budget wrappers already in the chain.

## 9. Side by side

| | Gemini (today) | Azure — Route A (`Azure.AI.OpenAI`) | Azure — Route B (`OpenAI` + v1) |
|---|---|---|---|
| New package | — | `Azure.AI.OpenAI` 2.9.0-beta.1 (prerelease) or 2.1.0 (stale GA) | none (optionally pin `OpenAI` 2.12.0) |
| Client type | `OpenAIClient` | `AzureOpenAIClient : OpenAIClient` | `OpenAIClient` |
| Credential | `ApiKeyCredential` | `ApiKeyCredential` (or `TokenCredential`) | `ApiKeyCredential` (or `BearerTokenPolicy`) |
| Endpoint carried as | `options.Endpoint` | **ctor argument** (no `Endpoint` on options) | `options.Endpoint` |
| Endpoint value | `…/v1beta/openai` | `https://<res>.openai.azure.com` | `https://<res>.openai.azure.com/openai/v1/` or `https://<res>.services.ai.azure.com/openai/v1/` |
| Route / versioning | OpenAI-compat path | `/openai/deployments/…?api-version=` (max `2025-04-01-preview`; GA build `2024-10-21`) | `/openai/v1/…`, no `api-version` |
| `GetChatClient(x)` | model id | **deployment name** | **deployment name** |
| → `IChatClient` | `.AsIChatClient()` → `OpenAIChatClient` | identical | identical |
| Request-side shim | `GeminiThoughtSignaturePolicy` (per-call policy) | none known | none known |
| Response-side shim | `GeminiCompatHandler` (`DelegatingHandler`) | none of ours; SDK carries max-token / stream-options fixes | none known |
| Provider-specific failure to handle | 400 INVALID_ARGUMENT, `MALFORMED_FUNCTION_CALL` | content filter: 400 on prompt, `finish_reason=content_filter` on completion | same |
| Strict structured output | available but undocumented-strength on free tier; repo sets nothing | GA, model list + limits documented | GA, same |
| Resilience handler attaches | no (hand-built `HttpClient`) | yes, via `Transport` over a factory client — mind the 10 s attempt timeout and the SDK's own retries | same |
| Cost | free tier, RPD-limited | metered | metered |

## 10. A minimal registration sketch

Not wired up — a sketch in this repo's DI style, Route B, with Route A shown as the one-line
difference. The shape deliberately keeps the existing resolution code (`ResolveAgentChatClient`,
the keyed per-agent clients, the OTel/metering/budget wrappers) untouched: only the construction of
the single `OpenAIClient` singleton moves behind a provider switch.

```csharp
// api/Agents/Configuration/AgentsOptions.cs (sketch)
public sealed class AiOptions
{
    public const string Section = "Ai";

    /// <summary>Which chat backend is live in this deployment. Enums persist by name; this one is
    /// configuration, not storage, but a usage row that records it is storage.</summary>
    public ChatProvider Provider { get; set; } = ChatProvider.Gemini;

    /// <summary>Default model id (Gemini) or <b>deployment name</b> (Azure) — same key, provider
    /// decides what the string means. Per-agent overrides live under Ai:Agents:&lt;agent&gt;.</summary>
    public string Model { get; set; } = "gemini-3.5-flash-lite";

    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public Dictionary<string, string> Agents { get; set; } = new();
}

public enum ChatProvider { Gemini, AzureOpenAI }
```

```csharp
// api/Agents/Configuration/AzureOpenAIChatClientFactory.cs (sketch)
// One factory per provider, both returning the same OpenAIClient contract. Gemini's two shims stay
// inside the Gemini factory and cannot reach this one.
internal static class AzureOpenAIChatClientFactory
{
    public const string HttpClientName = "azure-openai";

    public static OpenAIClient Create(IServiceProvider sp, AiOptions cfg)
    {
        var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") is { Length: > 0 } env
            ? env
            : cfg.ApiKey;

        // Route B: the v1 endpoint — "https://<resource>.openai.azure.com/openai/v1/" — so there is
        // no api-version to pin and no Azure-specific client to keep in step with the OpenAI SDK.
        // The transport is the factory's HttpClient, which is what lets ServiceDefaults' handler
        // attach at all (see §8 — configure that named client's timeouts; the defaults cancel a
        // healthy completion at 10s and retry on top of the SDK's own retries).
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

        return new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions
        {
            Endpoint = new Uri(cfg.Endpoint),   // must end in /openai/v1/
            Transport = new HttpClientPipelineTransport(http),
        });

        // Route A would be, instead — note the endpoint is the resource root, not the v1 path, and
        // that AzureOpenAIClientOptions has no Endpoint property:
        //   new AzureOpenAIClient(
        //       new Uri(cfg.Endpoint),
        //       new ApiKeyCredential(key),
        //       new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) });
        // It returns an AzureOpenAIClient, which *is* an OpenAIClient, so the registration below is
        // unchanged either way.
    }
}
```

```csharp
// api/Agents/Configuration/ChatClientServiceCollectionExtensions.cs (sketch)
public static IServiceCollection AddChatClients(this IServiceCollection services, IConfiguration config)
{
    var cfg = config.GetSection(AiOptions.Section).Get<AiOptions>() ?? new AiOptions();

    services.AddHttpClient(AzureOpenAIChatClientFactory.HttpClientName, c =>
        c.Timeout = TimeSpan.FromMinutes(2));   // and its own resilience options; see §8

    // The provider switch is one registration. Everything downstream — the keyed per-agent clients,
    // Instrument(), ResolveAgentChatClient and its budget wrapper — already speaks OpenAIClient.
    services.AddSingleton(sp => cfg.Provider switch
    {
        ChatProvider.AzureOpenAI => AzureOpenAIChatClientFactory.Create(sp, cfg),
        _ => GeminiChatClientFactory.Create(cfg),   // keeps GeminiCompatHandler + the signature policy
    });

    services.AddSingleton<IChatClient>(sp => Instrument(
        sp, sp.GetRequiredService<OpenAIClient>().GetChatClient(cfg.Model).AsIChatClient()));

    foreach (var (agentKey, model) in cfg.Agents)
    {
        services.AddKeyedSingleton<IChatClient>(agentKey, (sp, _) => Instrument(
            sp, sp.GetRequiredService<OpenAIClient>().GetChatClient(model).AsIChatClient()));
    }

    return services;
}
```

`api/Agents/Program.cs`'s Production guard (today: "no Gemini API key") becomes provider-aware —
it must demand the *active* provider's key, and on Azure also a non-empty endpoint and deployment
name, because an empty deployment name fails as a 404 at first call rather than at startup.

## 11. What this means for the seam's shape

* "Two factories, one contract" holds, and is *cheaper* than the map assumed: the contract is not
  `IChatClient` but `OpenAIClient` — both providers produce one, and everything the repo already
  built on top (keyed per-agent clients, OTel, metering, budgets) is untouched. A provider that was
  not OpenAI-shaped (Anthropic, Bedrock, a Foundry catalogue model on a different SDK) would break
  that and push the seam up to `IChatClient`; P1T-241 already rules catalogue models out of scope.
* The choice that actually needs deciding is **Route A vs Route B**, and the evidence points at
  Route B: no new package, no prerelease pin, no dated `api-version`, the same construction shape as
  the code on disk, and it is what Microsoft's own .NET docs and migration guide now prescribe
  (Route A's README still says to install with `--prerelease`, and its 2.9.0-beta.1 notes recommend
  dropping it). Route A's only advantage is the two in-SDK request fixes of §6.2, which exist to
  paper over the dated `api-version` surface that Route B does not use. Route A also keeps the
  `AzureSearchChatDataSource` / On-Your-Data extensions, which this repo does not want (it does its
  own retrieval through MCP).
* Three things force *some* provider-shaped difference regardless of route, and each wants a line in
  the ADR: `Model` means "deployment name" on Azure (§4); content filtering is a documented,
  Azure-only failure mode on both prompt (400) and completion (`finish_reason`) (§6.2); and the
  resilience handler becomes attachable, which is an opportunity and a foot-gun (§8).

## 12. Sources

All fetched 2026-09-20.

* Azure SDK for .NET — [`Azure.AI.OpenAI` CHANGELOG](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/openai/Azure.AI.OpenAI/CHANGELOG.md) · [migration-guidance.md](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/openai/Azure.AI.OpenAI/migration-guidance.md) · [AzureOpenAIClient.cs](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.9.0-beta.1/sdk/openai/Azure.AI.OpenAI/src/Custom/AzureOpenAIClient.cs) · [AzureOpenAIClientOptions.cs](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.9.0-beta.1/sdk/openai/Azure.AI.OpenAI/src/Custom/AzureOpenAIClientOptions.cs) · [AzureChatClient.cs](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.9.0-beta.1/sdk/openai/Azure.AI.OpenAI/src/Custom/Chat/AzureChatClient.cs)
* Microsoft Learn — [Azure OpenAI client library for .NET (README, documents 2.1.0)](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.openai-readme) · [v1 API / api-version lifecycle](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/api-version-lifecycle) · [SDK language support (.NET, tested with `OpenAI` 2.12.0)](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/supported-languages) · [Structured outputs](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/structured-outputs) · [Content filtering](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/concepts/content-filter) · [Build resilient HTTP apps](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
* NuGet — [`Azure.AI.OpenAI` version index](https://api.nuget.org/v3-flatcontainer/azure.ai.openai/index.json) · [2.9.0-beta.1 nuspec](https://api.nuget.org/v3-flatcontainer/azure.ai.openai/2.9.0-beta.1/azure.ai.openai.nuspec) · [2.1.0 nuspec](https://api.nuget.org/v3-flatcontainer/azure.ai.openai/2.1.0/azure.ai.openai.nuspec) · [`Microsoft.Extensions.AI.OpenAI` 10.9.0 nuspec](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai.openai/10.9.0/microsoft.extensions.ai.openai.nuspec) · [10.10.0 nuspec](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai.openai/10.10.0/microsoft.extensions.ai.openai.nuspec) · [`Microsoft.Agents.AI.OpenAI` 1.10.0 nuspec](https://api.nuget.org/v3-flatcontainer/microsoft.agents.ai.openai/1.10.0/microsoft.agents.ai.openai.nuspec)
* dotnet/extensions (release/10.9) — [OpenAIClientExtensions.cs](https://github.com/dotnet/extensions/blob/release/10.9/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIClientExtensions.cs) · [OpenAIChatClient.cs](https://github.com/dotnet/extensions/blob/release/10.9/src/Libraries/Microsoft.Extensions.AI.OpenAI/OpenAIChatClient.cs)
* This repo — `api/Agents/Configuration/GeminiServiceCollectionExtensions.cs`, `GeminiCompatHandler.cs`, `GeminiThoughtSignaturePolicy.cs`, `AgentsOptions.cs`, `api/Agents/Program.cs`, `api/ServiceDefaults/Extensions.cs`, `Directory.Packages.props`, `manuals/anthropic-gemini-ichatclient-mapping.md`
