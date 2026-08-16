# Anthropic structured output & tool_choice → Gemini / IChatClient mapping (research, P1T-106)

Learning-set research: Anthropic's structured-output methods and `tool_choice` semantics are the
reference vocabulary for tool reliability; this note maps each concept onto (a) the Gemini API we
actually call and (b) the Microsoft.Extensions.AI / Microsoft Agent Framework `IChatClient` layer
this repo's `api/Agents` service is built on — and records what our adapter already supports,
never sets, or has no analog for. Verified against live docs on 2026-08-16.

**Headline: Anthropic guarantees structured output by grammar-constrained decoding; Gemini offers
schema-constrained output (documented as validate-anyway) and forced/validated tool calls; M.E.AI
exposes both knobs (`ChatResponseFormat`, `ChatToolMode`) — but our Agents service sets neither:
every agent today relies on prompt-based JSON plus lenient parsers, the weakest tier in
Anthropic's own ranking. Anthropic's prefill technique has no analog anywhere in our stack — and
is itself dead on Claude ≥4.6 (400 error).**

## 1. Anthropic: the reference concepts

### Structured-output methods by schema-compliance strictness

| Rank | Method | Guarantee | Source |
|---|---|---|---|
| 1a | Native structured outputs — `output_config.format: {type: "json_schema", schema}` (GA, no beta header; old top-level `output_format` deprecated) | **Guaranteed** valid JSON matching the schema via grammar-constrained sampling ("no retries needed for schema violations") | [structured-outputs](https://platform.claude.com/docs/en/build-with-claude/structured-outputs) |
| 1b | Strict tool use — `strict: true` on the tool definition | **Guaranteed** tool `input` matches `input_schema` and tool `name` is valid (same grammar pipeline) | [strict-tool-use](https://platform.claude.com/docs/en/agents-and-tools/tool-use/strict-tool-use) |
| 2 | Forced tool without `strict` (`tool_choice: any`/`tool`, output shape = tool input schema) | A tool **will** be called, but inputs may drift ("`"2"` instead of `2`", missing required fields) | [define-tools](https://platform.claude.com/docs/en/agents-and-tools/tool-use/define-tools), [strict-tool-use](https://platform.claude.com/docs/en/agents-and-tools/tool-use/strict-tool-use) |
| 3 | Prompt-based formatting ("reply with ONLY this JSON") | No guarantee — "parsing errors … missing required fields … retries"; modern models are reliable-ish with retries | [structured-outputs](https://platform.claude.com/docs/en/build-with-claude/structured-outputs), [prompting-best-practices](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices) |
| 4 | Prefilled assistant response (start the assistant turn with `{`) | Steering only, never validation — and **rejected (400) on Claude ≥4.6 / Mythos**; docs ship a migration table (JSON forcing → structured outputs, classification → enum tool, preamble-skip → system prompt) | [prompting-best-practices § migrating away from prefills](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-prompting-best-practices) |

Native structured outputs vs strict tools split by purpose, not strength: output format controls
*what Claude says*, strict tools validate *how Claude calls functions*; combinable in one request.
Constraints worth teaching: `additionalProperties: false` required everywhere, no recursive
schemas or numeric/length bounds; incompatible with citations and prefilling; first-request
grammar-compilation latency (24 h compiled-grammar cache); `refusal` stop reason and `max_tokens`
truncation can still yield non-schema output ([structured-outputs](https://platform.claude.com/docs/en/build-with-claude/structured-outputs)).
The docs' recommended extraction recipe is `tool_choice: any` **plus** `strict: true` — forced
call and guaranteed shape together.

### tool_choice semantics

All from [define-tools](https://platform.claude.com/docs/en/agents-and-tools/tool-use/define-tools)
unless noted:

- `auto` — model decides; default when `tools` present.
- `any` — must call *some* provided tool.
- `tool` + `name` — must call *that* tool.
- `none` — no tools; default when no `tools` in the request.
- Mechanism: with `any`/`tool` the API itself prefills the assistant turn, so no natural-language
  preamble is emitted (the one prefill that survives).
- `disable_parallel_tool_use: true` lives *inside* `tool_choice` (not top-level): `auto` → at most
  one call, `any`/`tool` → exactly one ([parallel-tool-use](https://platform.claude.com/docs/en/agents-and-tools/tool-use/parallel-tool-use)).
- Manual extended thinking is incompatible with `any`/`tool` (error); only `auto`/`none` work.
  Adaptive thinking (e.g. Opus 5 default-on) does support forced tool use.
- Changing `tool_choice` invalidates cached message blocks (tool definitions/system stay cached).

### Tool-description best practices

From [define-tools § best practices](https://platform.claude.com/docs/en/agents-and-tools/tool-use/define-tools):

- "Provide extremely detailed descriptions. This is by far the most important factor in tool
  performance" — what it does, when to use it *and when not*, what each parameter means, what the
  tool does **not** return; 3–4+ sentences per tool.
- Input format specs: a dedicated optional `input_examples` array on the tool definition
  (schema-validated, ~20–200 tokens each) teaches optional-parameter usage and complex formats.
- Disambiguating similar tools: consolidate related operations into one tool with an `action`
  parameter ("fewer, more capable tools reduce selection ambiguity") and namespace tool names
  (`github_list_prs`, `slack_send_message`).
- Return only high-signal information from tools; deeper guidance in
  [Writing tools for agents](https://www.anthropic.com/engineering/writing-tools-for-agents).

### Multi-tool sequencing

- Chaining is the agentic loop: each dependent step is its own round trip — model asks, you
  execute, you report back — so prerequisite data is fetched on one turn and consumed on the next
  ([how-tool-use-works](https://platform.claude.com/docs/en/agents-and-tools/tool-use/how-tool-use-works)).
- Parallel is the default *within* a turn; the docs' fix for dependent calls landing in one batch:
  run sequentially, stop on first failure, return `is_error: true` ("Not executed: the preceding
  call failed") for the rest — Claude reissues next turn; and prompt "Only batch tool calls that
  are independent of each other" ([parallel-tool-use](https://platform.claude.com/docs/en/agents-and-tools/tool-use/parallel-tool-use)).
- Missing prerequisite parameters: return `is_error: true` with an instructive message — the model
  retries 2–3 times; `strict: true` eliminates the malformed-input class entirely
  ([handle-tool-calls](https://platform.claude.com/docs/en/agents-and-tools/tool-use/handle-tool-calls), [overview](https://platform.claude.com/docs/en/agents-and-tools/tool-use/overview)).

## 2. Gemini API mapping

Doc-landscape caveat: the ai.google.dev *guides* have been rewritten around Gemini 3.x and the new
Interactions API; classic `generateContent` semantics (what applies to our
`gemini-flash-lite`-class models) live in the API reference pages.

### Structured output

- `generationConfig.responseMimeType`: `text/plain` (default), `application/json`, `text/x.enum`
  ("ENUM as a string response") ([generate-content reference](https://ai.google.dev/api/generate-content)).
- `responseSchema` — "a select subset of an OpenAPI 3.0 schema object" — is now **marked
  deprecated** in the reference in favor of `responseJsonSchema`. Supported fields include `type`
  (STRING/NUMBER/INTEGER/BOOLEAN/ARRAY/OBJECT/NULL), `format`, `enum[]`, `nullable`, `properties`,
  `required[]`, `items`, `minItems`/`maxItems`, `minimum`/`maximum`, `anyOf`, and the non-standard
  `propertyOrdering[]` ([generate-content reference](https://ai.google.dev/api/generate-content)).
- `responseJsonSchema` accepts real JSON Schema: `$id`/`$defs`/`$ref`/`$anchor`, `enum`,
  `prefixItems`, `additionalProperties`, `oneOf` (treated as `anyOf`), `propertyOrdering`;
  requires `responseMimeType` and excludes `responseSchema`; cyclic refs only in non-required
  properties ([generate-content reference](https://ai.google.dev/api/generate-content)).
- **Compliance is strong-but-validate, not guaranteed**: the guide asserts syntactically correct
  JSON but says "always validate values in your application" and "implement robust error handling
  for schema-compliant but semantically incorrect outputs"; large/deep schemas may be rejected
  ([structured-output guide](https://ai.google.dev/gemini-api/docs/structured-output)). Contrast:
  Anthropic documents its structured outputs as a hard grammar guarantee.

### Enums, nullable, optional

- Enum three ways: `text/x.enum` MIME type (whole response is one enum string — Anthropic's
  closest analog is a single-enum-field schema); `enum` on STRING fields; `enum` on
  number/integer fields ([reference](https://ai.google.dev/api/generate-content),
  [guide](https://ai.google.dev/gemini-api/docs/structured-output)).
- Nullable: classic `Schema.nullable` boolean; the JSON-Schema path uses `type: ["...", "null"]`.
- Optional vs required: `required[]` on objects, plus `additionalProperties` in the guide;
  `propertyOrdering` fixes output key order (non-standard, Gemini-specific — no Anthropic analog).
- Recursion via `"$ref": "#"` is documented ([guide](https://ai.google.dev/gemini-api/docs/structured-output)) —
  the opposite of Anthropic, which forbids recursive schemas.

### Function calling and forced modes

- `toolConfig.functionCallingConfig.mode` ([caching/ToolConfig reference](https://ai.google.dev/api/caching)):
  - `AUTO` (default) — model decides function call vs text → Anthropic `tool_choice: auto`.
  - `ANY` — "constrained to always predicting a function call only"; with `allowedFunctionNames`
    limited to that set → Anthropic `any`, and `allowedFunctionNames: ["x"]` ≈ Anthropic
    `tool: {name: "x"}` (Gemini has no dedicated single-tool type — the allowlist plays that role).
  - `NONE` — no function calls → Anthropic `none`.
  - `VALIDATED` — model decides, **but validates function calls with constrained decoding**
    (also honors `allowedFunctionNames`) → closest analog of Anthropic's `strict: true` tools,
    but chosen per-request rather than per-tool definition.
- Tool declarations: `FunctionDeclaration` with `name`, `description`, `parameters` (OpenAPI
  Schema) or `parametersJsonSchema` (mutually exclusive), plus `response`/`responseJsonSchema`
  for declared return shapes ([generate-content reference](https://ai.google.dev/api/generate-content)).
- Parallel and compositional calling both documented: "calling multiple functions in a single turn
  (parallel) and in sequence (compositional... get location first, then get weather for that
  location)" ([function-calling guide](https://ai.google.dev/gemini-api/docs/function-calling)) —
  same prerequisite-then-dependent chaining model as Anthropic's agentic loop.
- The new Interactions API renames this `generation_config.tool_choice`
  (auto/any/none/validated, `allowed_tools`) — vocabulary converging on Anthropic/OpenAI's.

### Combining, and free tier

- Structured output **combined with** function calling in one request is documented only for
  Gemini 3 series ("Preview... Gemini 3 lets you combine Structured Outputs with built-in
  tools, including... Function Calling") — for 2.5-era models the guides frame them as
  alternatives; don't count on combining on `flash-lite`
  ([structured-output guide](https://ai.google.dev/gemini-api/docs/structured-output)).
- **Nothing here is tier-gated**: the pricing and rate-limit pages gate only RPM/TPM/RPD, Batch,
  Flex, and grounding quotas — structured output and function calling (incl. ANY/VALIDATED) are
  plain `generateContent` features available on the free tier
  ([pricing](https://ai.google.dev/gemini-api/docs/pricing),
  [rate-limits](https://ai.google.dev/gemini-api/docs/rate-limits)).

### The OpenAI-compat layer — our actual wire format

From [openai compatibility](https://ai.google.dev/gemini-api/docs/openai) (the endpoint this repo
uses):

- **Structured output: supported** — the docs demonstrate `response_format` via the SDK helpers
  (`chat.completions.parse` / `zodResponseFormat`, i.e. `json_schema`); raw `json_object` isn't
  explicitly documented.
- **tool_choice: only `"auto"` is documented.** `required`, `none`, and named-function forcing are
  not shown (undocumented, not declared unsupported) — forced calling is only *documented* on the
  native API (ANY/VALIDATED). Treat `ChatToolMode.RequireAny`/`RequireSpecific` over the compat
  endpoint as test-before-trust.
- Compat layer is **beta** ("Support for the OpenAI libraries is still in beta"); unknown
  parameters are silently ignored; Gemini-specific features need the `extra_body` escape hatch.

## 3. IChatClient / M.E.AI / MAF mapping — and what this repo does

Verified against Microsoft Learn plus dotnet/extensions source on `main` (Learn currently links
commit `b10f9c0a` as its source snapshot).

### Response format

- `ChatOptions.ResponseFormat` takes a `ChatResponseFormat`: `ChatResponseFormat.Text`,
  `ChatResponseFormat.Json` (schema-less JSON mode), or `ChatResponseFormat.ForJsonSchema(...)`
  → sealed `ChatResponseFormatJson` with `Schema`/`SchemaName`/`SchemaDescription`
  ([ChatResponseFormat](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatresponseformat),
  [ChatResponseFormatJson](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatresponseformatjson),
  [ChatOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatoptions)).
- The typed helper `ChatClientStructuredOutputExtensions.GetResponseAsync<T>` has a
  `useJsonSchemaResponseFormat` parameter (`bool?`, **defaults to `true`** — docs warn it "may
  error if the model does not support native structured output"): when true it sets
  `ResponseFormat = ForJsonSchema<T>()` and adds no prompt (the provider is expected to teach the
  model the schema). With `false` it falls back to `ChatResponseFormat.Json` **plus an injected
  prompt** "Respond with a JSON value conforming to the following schema: …" — the documented
  fallback for providers without native schema support. Non-object `T`s get wrapped in a
  `{"data": ...}` envelope; any pre-set `ResponseFormat` is overwritten
  Source comment: "When using native structured output, we don't add any additional prompt,
  because the LLM backend is meant to do whatever's needed to explain the schema to the LLM"
  ([GetResponseAsync\<T\>](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatclientstructuredoutputextensions.getresponseasync),
  [source](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/ChatClientStructuredOutputExtensions.cs)).

### Forced function calling

- `ChatOptions.ToolMode` takes a `ChatToolMode`: `Auto` (optional), `None` (no tool calls),
  `RequireAny` (must call some tool), `RequireSpecific(functionName)` (must call that tool —
  a `RequiredChatToolMode` carrying `RequiredFunctionName`)
  ([ChatToolMode](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chattoolmode),
  [RequiredChatToolMode](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.requiredchattoolmode)).
  `ChatOptions.AllowMultipleToolCalls` is the M.E.AI analog of Anthropic's
  `disable_parallel_tool_use` (inverted).
- **`FunctionInvokingChatClient` auto-resets a required tool mode after the first iteration** —
  source comment: "We have to reset the tool mode to be non-required after the first iteration,
  as otherwise we'll be in an infinite loop" — so `RequireAny`/`RequireSpecific` forces only the
  *first* model call of the loop; later iterations run with `ToolMode = null`
  ([source](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/FunctionInvokingChatClient.cs)).
  Loop defaults: `MaximumIterationsPerRequest` 40, `MaximumConsecutiveErrorsPerRequest` 3,
  `AllowConcurrentInvocation` false (sequential tool execution within a response),
  `TerminateOnUnknownCalls` false (unknown tool → auto "tool not found" result)
  ([FunctionInvokingChatClient](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.functioninvokingchatclient)).

### Tool metadata

- `AIFunction` carries `Name`, `Description`, `JsonSchema` (parameters), `ReturnJsonSchema`, and
  `AdditionalProperties`; `AIFunctionFactory` derives both schemas automatically and maps
  `[Description]` attributes on the method and on each parameter
  ([AIFunction](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.aifunction),
  [AIFunctionFactory.Create](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.aifunctionfactory.create)).
  There is **no analog of Anthropic's `input_examples`** — examples must be folded into the
  description text.

### MAF surface

- `ChatClientAgentOptions.ChatOptions` sets agent-level defaults (so `ResponseFormat`/`ToolMode`
  are settable per agent), and `ChatClientAgentRunOptions.ChatOptions` applies per run — combined
  with the agent defaults, run options taking precedence, collections like `Tools` unioned; the
  base `AgentRunOptions` also carries its own `ResponseFormat` property
  ([ChatClientAgentOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.chatclientagentoptions?view=agent-framework-dotnet-latest),
  [ChatClientAgentRunOptions.ChatOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.chatclientagentrunoptions.chatoptions?view=agent-framework-dotnet-latest)).
  Our agents use the `ChatClientAgent(chatClient, instructions, name, description, tools)`
  convenience constructor and pass `null` run options, which is why no ChatOptions flow today.

### What api/Agents does today (code-verified)

Our stack: `Microsoft.Agents.AI` 1.10.0 + `Microsoft.Extensions.AI` 10.7.0 +
`Microsoft.Extensions.AI.OpenAI` 10.7.0, net10.0
([CvManager.Agents.csproj](../api/Agents/CvManager.Agents.csproj)). Gemini is reached through its
**OpenAI-compatibility endpoint** (`https://generativelanguage.googleapis.com/v1beta/openai`,
default model `gemini-flash-lite-latest`) via `OpenAIClient.GetChatClient(model).AsIChatClient()`
([GeminiServiceCollectionExtensions.cs](../api/Agents/Configuration/GeminiServiceCollectionExtensions.cs)) —
so the *compat layer's* feature surface, not the native API's, is our ceiling.

| Concept | Repo status |
|---|---|
| `ChatOptions.ResponseFormat` | **Never set.** No agent passes ChatOptions at all; structured replies are prompt-based ("reply with ONLY this JSON — no prose, no markdown fences", [ResumeIngestionAgent.cs](../api/Agents/Agents/ResumeIngestionAgent.cs)) parsed leniently ([MatchAnswerParser.cs](../api/Agents/Staffing/MatchAnswerParser.cs) regex-scans markdown and returns nulls, never throws) — Anthropic's tier 3. |
| `ChatToolMode` (forced calls) | **Never set** — tool use is always model-optional (`auto`). Reliability comes from instructions plus narrowing the tool list per agent (e.g. MatchAgent gets only `cv_get`, [MatchAgent.cs](../api/Agents/Agents/MatchAgent.cs)). |
| Tool metadata | MCP tools carry `[Description]` on tool + every parameter ([api/Mcp/Tools](../api/Mcp/Tools)); flows to the model via MAF `ChatClientAgent(tools:)`. No use-case examples / anti-examples yet — Anthropic's 3–4-sentence bar is not met. |
| Multi-tool sequencing | Prompt-encoded numbered procedures (skill_list → employee_create_draft → children; "at most 2 retries, then skip"; hard-stop rule) in [ResumeIngestionAgent.cs](../api/Agents/Agents/ResumeIngestionAgent.cs) — matches Anthropic's guidance in spirit, enforced only by instructions. |
| Malformed function calls | Handled *below* the abstraction: [GeminiCompatHandler.cs](../api/Agents/Configuration/GeminiCompatHandler.cs) retries `MALFORMED_FUNCTION_CALL` up to 3× and normalizes unknown `finish_reason` values to "stop"; [GeminiThoughtSignaturePolicy.cs](../api/Agents/Configuration/GeminiThoughtSignaturePolicy.cs) injects the `skip_thought_signature_validator` sentinel. This is the compensating machinery for not using any strict/forced modes. |

## 4. Concept-by-concept mapping table

| Anthropic concept | Gemini native API | Gemini OpenAI-compat (our wire) | IChatClient / MAF | api/Agents today |
|---|---|---|---|---|
| Native structured outputs (`output_config.format`, grammar-guaranteed) | `responseMimeType: application/json` + `responseJsonSchema` (or deprecated `responseSchema`) — validate-anyway, not guaranteed | `response_format` json_schema (via SDK parse helpers) | `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<T>()`; typed `GetResponseAsync<T>` | Not used |
| Strict tool use (`strict: true` per tool) | `functionCallingConfig.mode: VALIDATED` (constrained decoding of calls; per request, not per tool) | Not documented | No abstraction — provider-specific | Compensated below the stack (MALFORMED_FUNCTION_CALL retry ×3) |
| `tool_choice: auto` | `mode: AUTO` (default) | `tool_choice: "auto"` (only documented value) | `ChatToolMode.Auto` (default) | Implicit default |
| `tool_choice: any` | `mode: ANY` | undocumented (`required` untested) | `ChatToolMode.RequireAny` | Not used |
| `tool_choice: tool` + name | `mode: ANY` + `allowedFunctionNames: ["x"]` (no dedicated single-tool type) | undocumented (named forcing untested) | `ChatToolMode.RequireSpecific("x")` (one-shot under `UseFunctionInvocation` — auto-reset after first iteration) | Not used; analog is narrowing the tool *list* per agent |
| `tool_choice: none` | `mode: NONE` | undocumented | `ChatToolMode.None` | Achieved by passing no tools |
| `disable_parallel_tool_use` | n/a (parallel calling is model behavior) | n/a | `ChatOptions.AllowMultipleToolCalls` (inverted); `FunctionInvokingChatClient.AllowConcurrentInvocation` (execution side, default false) | Not used |
| Detailed tool descriptions | `FunctionDeclaration.description` + per-param schema descriptions | Standard OpenAI `tools` JSON | `AIFunction.Name/Description/JsonSchema` (from `[Description]` via `AIFunctionFactory` / MCP) | MCP `[Description]` on tools + params — short one-liners |
| `input_examples` on tool | No field — fold into description | No field | No field — fold into description or `AdditionalProperties` (not sent) | Not used |
| Enum-constrained answer | `text/x.enum` MIME or `enum` schema field | enum inside json_schema | enum inside `ForJsonSchema` schema | Not used (bands parsed by regex) |
| Sequential prerequisite chaining | Compositional function calling (documented) | Works (chat-completions loop) | `FunctionInvokingChatClient` loop (max 40 iterations, 3 consecutive errors) — MAF `ChatClientAgent` uses it | Prompt-encoded numbered procedures; works in practice |
| Prefilled assistant response | — | — | — | — (no analog anywhere; dead on Claude ≥4.6 too) |

## 5. Explicit gaps — no Gemini/IChatClient analog

1. **Prefill (`{`-priming)** — no Gemini analog, no OpenAI-compat analog, no `IChatClient`
   abstraction for partial-assistant continuation; Anthropic itself now 400s it on ≥4.6 models.
   Teach it as a historical technique whose replacement is native structured output.
2. **Per-tool strictness** — Anthropic's `strict: true` lives on the tool definition; Gemini's
   `VALIDATED` is per-request, and M.E.AI has no strictness knob at all: `AIFunction` carries a
   schema but no "enforce it" flag; enforcement is whatever the provider does.
3. **Anthropic's schema guarantee** — Gemini's structured output is documented as
   validate-anyway; nothing in our stack can promise "no JSON.parse errors" the way Anthropic's
   grammar-constrained decoding does.
4. **`input_examples`** — Anthropic-only field; everywhere else examples must live in
   description text.
5. **Forced tool calls over the OpenAI-compat endpoint** — `ChatToolMode.RequireAny/
   RequireSpecific` maps to OpenAI `tool_choice: required`/named, which the Gemini compat docs
   never mention: undocumented behavior, needs a live test before we rely on it (native
   `ANY`/`allowedFunctionNames` is the documented route, but our adapter doesn't speak native).
6. **Persistent forcing** — even where forcing works, `FunctionInvokingChatClient` deliberately
   drops the required mode after the first iteration; Anthropic's `tool_choice` persists per
   request. A "must end by calling tool X after gathering data" policy has no direct mapping.
7. **Thinking × forcing interplay** — Anthropic documents manual extended thinking as
   incompatible with `any`/`tool`; neither Gemini nor M.E.AI documents an equivalent constraint
   (our thought-signature shim is adjacent but about replay, not forcing).

## Verdict for the learning set (and cheap wins for this repo)

- Teach the strictness ladder as provider-portable: *grammar-guaranteed* (Anthropic only) →
  *schema-constrained, validate anyway* (Gemini `responseJsonSchema`, OpenAI-compat
  `response_format`, `ChatResponseFormat.ForJsonSchema`) → *forced tool as output shape* →
  *prompt + lenient parser* (where this repo sits today).
- Cheapest repo upgrade with no new dependencies: set
  `ChatOptions.ResponseFormat = ForJsonSchema` (or use `GetResponseAsync<T>`) on the agents that
  end with "reply with ONLY this JSON" (ResumeIngestion) or regex parsing (Match score/band) —
  the compat layer documents `response_format` support, so this should replace two lenient
  parsers with schema-shaped replies. Verify `ChatToolMode.RequireAny` against the compat
  endpoint before teaching it as available.

## Live probe results (P1T-115, 2026-08-16)

The "test-before-trust" verification the verdict above called for —
`tests/Agents.Tests/CompatEndpointProbeTests.cs` (`Category=live`), run against the real compat
endpoint on the pinned default model `gemini-3.5-flash-lite`:

| Knob | Probe | Result |
| --- | --- | --- |
| `GetResponseAsync<T>` with `useJsonSchemaResponseFormat: true` (native `response_format` json_schema) | Schema with string enum + nullable int + required array | **PASS** — schema-valid, enum and nullable extracted correctly |
| `useJsonSchemaResponseFormat: false` (JSON mode + schema injected into the prompt) | Same schema | **PASS** |
| `ChatToolMode.RequireAny` | Neutral prompt ("Say hello."), one trivial tool | **PASS** — a function call came back despite the compat docs documenting only `tool_choice: "auto"` |
| `ChatToolMode.RequireSpecific("lookup_office_city")` | Same, named function | **PASS** — the named function was called |

**Decisions locked (per the P1T-111 decision rule):**

- Structured output method for extraction and the parser conversions: **native
  `ForJsonSchema`** (`useJsonSchemaResponseFormat: true`); the `false` fallback stays
  available and probe-covered but is not the default path.
- Tool forcing (P1T-112): **available on the compat wire** — `RequireAny`/`RequireSpecific`
  are usable for first-call forcing (still one-shot under `FunctionInvokingChatClient`).
  The Capture-Verify Guard ships regardless, per the grilling.

The probes stay in the suite as regression canaries: a red probe means Google changed the
compat wire's behavior, not that our code broke.
