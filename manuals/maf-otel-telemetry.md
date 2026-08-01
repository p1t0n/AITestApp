# Per-request tracing and per-agent cost/latency with MAF + OTel (research, P1T-83)

What it takes to get one distributed trace per user request (SPA → Agents → MCP → Postgres) and
per-agent/per-step cost and latency numbers out of the stack we run: `Microsoft.Agents.AI` +
`Microsoft.Agents.AI.Workflows` 1.10.0, `Microsoft.Extensions.AI` 10.7.0, `ModelContextProtocol`
1.4.0, Npgsql 10, ASP.NET Core on net10.0. Verified against the shipped 1.10.0/10.7.0 package
XML docs and DLL string tables in the local NuGet cache (**primary**), plus official docs.

**Headline: everything in the hot path already ships OpenTelemetry instrumentation — chat calls,
agent runs, workflow executor steps, MCP RPCs, HTTP, and Postgres. None of it emits until we (a)
wrap the right objects with the opt-in decorators and (b) host an OTel `TracerProvider`/
`MeterProvider` that subscribes to their sources and exports OTLP. The repo currently references
zero OpenTelemetry packages, so this is additive wiring, not a rework. Token counts land both as
span attributes/metrics for free and stay available for the existing `AgentUsage` DB ledger —
which needs three new columns (latency, trace id, step) and one fix (model id from the response,
not config) to feed the tiering decision.**

## 1. What the packages emit out of the box (all opt-in, verified in 1.10.0 / 10.7.0)

| Layer | How to enable | ActivitySource (default) | Spans | Metrics |
|---|---|---|---|---|
| Chat call (M.E.AI 10.7.0) | `chatClient.AsBuilder().UseOpenTelemetry(loggerFactory, sourceName, cfg)` → `OpenTelemetryChatClient` | `Experimental.Microsoft.Extensions.AI` | `chat {model}`, `execute_tool {tool}` (function-invocation layer, same source) | `gen_ai.client.operation.duration`, `gen_ai.client.token.usage` histograms (+ `…time_to_first_chunk`, `…time_per_output_chunk` when streaming) |
| Agent run (Agents.AI 1.10.0) | `agent.AsBuilder().UseOpenTelemetry(sourceName, cfg)` → `OpenTelemetryAgent` | `Experimental.Microsoft.Agents.AI` | `invoke_agent {name}` with `gen_ai.agent.id/name/description`, `gen_ai.operation.name`, `gen_ai.provider.name` | none in 1.10.0 (no meter in the DLL — token/duration metrics come from the chat-client layer) |
| Workflow (Workflows 1.10.0) | `new WorkflowBuilder(...).WithOpenTelemetry(options, activitySource?)` | `Microsoft.Agents.AI.Workflows` (no `Experimental.` prefix) | `workflow.build`, `workflow_invoke`, **`executor.process` per executor**, `edge_group.process`, `message.send`; attrs `workflow.id/name`, `executor.id/type` | none |
| MCP (SDK 1.4.0) | nothing to wrap — built into client and server; just subscribe | `Experimental.ModelContextProtocol` (ActivitySource **and** Meter) | per JSON-RPC request, attrs `mcp.method.name`, `mcp.session.id`, `rpc.response.status_code` | `mcp.client.operation.duration`, `mcp.server.operation.duration`, `…session.duration` |
| HTTP client (net10 BCL) | built-in; `AddSource("System.Net.Http")` | `System.Net.Http` | `{method}` per request, full semconv attrs since .NET 9 (no contrib package needed) | `http.client.*` (built-in meters) |
| ASP.NET Core | `OpenTelemetry.Instrumentation.AspNetCore` (or built-in `Microsoft.AspNetCore` source) | `Microsoft.AspNetCore` | one server span per request; reads incoming `traceparent` automatically | `http.server.*` |
| Npgsql 10 (under EF Core) | built-in; `AddSource("Npgsql")` / `AddMeter("Npgsql")` | `Npgsql` | one span per command (`db.query.text`, `db.namespace`, `db.operation.name` — v10 uses current db semconv) | `db.client.operation.duration`, connection-pool gauges |

Facts worth pinning:

- **Yes, `FunctionExecutor` steps get spans in 1.10.0.** `WorkflowTelemetryOptions` carries
  `DisableWorkflowBuild/WorkflowRun/ExecutorProcess/EdgeGroupProcess/MessageSend` toggles (all
  enabled by default once `WithOpenTelemetry` is called) — the `executor.process` span per
  executor is exactly our per-step latency. Verified in the 1.10.0 nupkg XML docs and DLL string
  table (`executor.process`, `executor.id`, `executor.type`, `workflow_invoke`…). The prior
  manual's "OpenTelemetry built in" claim is true but **opt-in per `WorkflowBuilder`** — our
  pipeline never calls it today (`api/Agents/Staffing/StaffingPipeline.cs:149`).
- `OpenTelemetryChatClient` implements **GenAI semconv v1.41 — experimental, output subject to
  change** ([API docs remark](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.opentelemetrychatclient)).
  Span attrs verified in the 10.7.0 DLL: `gen_ai.request.model`, `gen_ai.response.model`,
  `gen_ai.usage.input_tokens/output_tokens`, `gen_ai.response.finish_reasons`, `gen_ai.response.id`,
  `server.address/port`, `error.type`.
- **Sensitive content is off by default.** Prompts/responses/tool args (`gen_ai.input.messages`,
  `gen_ai.output.messages`, `gen_ai.system_instructions`, `gen_ai.tool.call.arguments/result`)
  are only recorded when `EnableSensitiveData = true` or env
  `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=true` (chat client and `OpenTelemetryAgent`
  both honor the env var; the property wins). Workflow payloads (`executor.input/output`,
  `message.content`) have their own `WorkflowTelemetryOptions.EnableSensitiveData` — default
  false, **no env var**. Keep all of it off: our showcase traces would otherwise carry CVs.
- `OpenTelemetryAgent` **auto-wires an `OpenTelemetryChatClient`** around the agent's chat client
  for the run (`_autoWireChatClient`/`ForwardingChatClient` in the 1.10.0 XML docs), so
  instrumenting both layers is redundant-but-harmless when sensitive data is off; the docs warn
  about duplicated content only when it's on. Enabling only the chat-client layer is enough for
  cost numbers; the agent layer adds the `invoke_agent` grouping span.
- The current MAF docs name the default agent source `Experimental.Microsoft.Agents.AI` — the
  1.10.0 DLL contains exactly that string, so the pin matches the docs.
- MCP SDK diagnostics are prefixed `Experimental.` — names may change across releases.

## 2. What still needs manual work

- **Host the OTel SDK** in both services (`api/Agents`, `api/Mcp`): packages
  `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
  `OpenTelemetry.Instrumentation.AspNetCore`; then `AddOpenTelemetry().WithTracing(t => t
  .AddAspNetCoreInstrumentation().AddSource(<table above>).AddOtlpExporter())` + `.WithMetrics`.
  Nothing emits without this — decorators no-op when `ActivitySource.HasListeners()` is false.
- **Wrap the chat clients once** in `GitHubModelsServiceCollectionExtensions` (both the default
  and the keyed per-agent clients) — one seam covers all four agents plus the staffing narrative
  call.
- **`.WithOpenTelemetry()` on the staffing `WorkflowBuilder`** (one line at
  `StaffingPipeline.cs:149`) for per-step spans.
- **MCP needs zero code** on either side — instrumentation ships in the SDK; subscribe to
  `Experimental.ModelContextProtocol` in both services.
- **EF Core/Npgsql needs zero code** — Npgsql's tracing is native (`AddSource("Npgsql")`);
  the EF-level contrib instrumentation is optional and adds little over the command spans
  ([Npgsql tracing docs](https://www.npgsql.org/doc/diagnostics/tracing.html),
  [metrics docs](https://www.npgsql.org/doc/diagnostics/metrics.html)).
- **SSE endpoint needs nothing for correctness**: the pipeline runs inside the request, so the
  ASP.NET Core server span covers the whole stream and parents every step/chat/MCP span. Optional
  polish: `Activity.Current?.AddEvent(...)` per `StaffingProgressEvent`, and stamping the trace id
  into the SSE `report` event so a demo can jump from UI result to trace.

## 3. Token/cost capture

- Usage surfaces on **`ChatResponse.Usage` → `UsageDetails.InputTokenCount/OutputTokenCount/
  TotalTokenCount`** ([UsageDetails docs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.usagedetails))
  — exactly what our agents already harvest into `AgentReply` (e.g.
  `api/Agents/Agents/MatchAgent.cs:60`) and `UsageMeter` persists.
- `OpenTelemetryChatClient` records the same numbers **twice more for free**: span attributes
  (`gen_ai.usage.input_tokens/output_tokens`) and the `gen_ai.client.token.usage` histogram,
  dimensioned by `gen_ai.token.type` and `gen_ai.request.model` — per-model token aggregates in
  the dashboard with no code.
- **Cleanest DB-ledger enrichment: a `DelegatingChatClient`** inserted at the same registration
  seam (the M.E.AI middleware pattern; `OpenTelemetryChatClient` itself is one). It observes what
  the endpoint-level `UsageMeter` can't see cheaply: `response.ModelId` (the model **the provider
  actually served**, vs. today's config lookup in `UsageMeter.cs:27` — which reads
  `GitHubModels:*` keys and will silently mislabel rows after the Gemini migration), wall-clock
  latency around `GetResponseAsync`, and `Activity.Current?.TraceId` for trace↔row correlation.
  Per-step attribution rides an ambient tag (an `AsyncLocal<string>` scope set by each run
  service/pipeline step, or `ChatOptions.AdditionalProperties`) — the staffing pipeline already
  meters per run-service (`shortlist`, `match` per candidate, `staffing` narrative), so the step
  label mostly exists; it just isn't stored with latency.
- Widen `AgentUsage` (`api/Domain/Entities/AgentUsage.cs`) with `LatencyMs`, `TraceId`, `Step`
  (nullable) — append-only migration, no read-path change; `UsageService` aggregations continue
  untouched.
- Cost is **derived, not captured**: rows already hold model + tokens, so `$ = tokens × price`
  is a lookup table (Gemini published prices) applied at query time. No pipeline work.

## 4. Exporter for the local-first free showcase

| Option | Traces | Metrics | Setup | Verdict |
|---|---|---|---|---|
| **Aspire dashboard standalone** — `mcr.microsoft.com/dotnet/aspire-dashboard:latest`, UI :18888, OTLP gRPC :18889, OTLP HTTP :18890; in-memory; token login by default, `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true` for local ([docs](https://aspire.dev/dashboard/standalone/)) | ✅ | ✅ (+ structured logs) | 1 compose service + `OTEL_EXPORTER_OTLP_ENDPOINT` env | **Use.** Zero config, renders gen_ai spans well; MAF docs themselves recommend it for local dev |
| Jaeger v2 all-in-one — `cr.jaegertracing.io/jaegertracing/jaeger:2.x`, native OTLP :4317/:4318, UI :16686 ([docs](https://www.jaegertracing.io/docs/latest/getting-started/)) | ✅ | ❌ | 1 compose service | Skip — no metrics view, so no token/latency histograms |
| Grafana + Tempo + Prometheus | ✅ | ✅ | 3 services + per-tool config files | Skip for showcase — real dashboards, not worth the setup now; the OTLP wiring is identical if we graduate later |
| Azure Monitor / App Insights | ✅ | ✅ | needs an Azure resource | **Not allowed by default in this project** |

Add `aspire-dashboard` to `docker-compose.yml` next to postgres/keycloak; both services get
`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318` (HTTP) or `:4317` (gRPC). Effort: minutes.

## 5. Trace propagation SPA → Agents → MCP → Postgres

- **Automatic once tracing is on (server-side):** ASP.NET Core reads the incoming W3C
  `traceparent` and creates the request activity; `HttpClient` injects `traceparent` into
  outgoing requests whenever a current activity exists — both built into the BCL/hosting, W3C
  format is the .NET default ([.NET networking tracing](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/telemetry/tracing),
  [built-in activities](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-builtin-activities)).
  So Agents → MCP → Postgres is one trace with zero propagation code: MCP travels over
  `HttpClient`, and the MCP C# SDK **additionally** injects/extracts trace context inside the
  JSON-RPC `_meta` field itself and folds MCP attributes into an enclosing `execute_tool` span
  instead of duplicating it
  ([Diagnostics.cs](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/src/ModelContextProtocol.Core/Diagnostics.cs)).
  Npgsql spans parent under the MCP request span automatically.
- **The SPA is the only gap.** Browsers don't send `traceparent` on their own. Options:
  `@opentelemetry/instrumentation-fetch` injects it via `propagation.inject` (same-origin by
  default; cross-origin requires `propagateTraceHeaderCorsUrls` **and** the API allowing the
  `traceparent` header in CORS —
  [fetch.ts](https://github.com/open-telemetry/opentelemetry-js/blob/main/experimental/packages/opentelemetry-instrumentation-fetch/src/fetch.ts)),
  or hand-roll a random `traceparent: 00-<32hex>-<16hex>-01` header on our API fetch wrapper.
  **Without it, nothing breaks** — every trace simply roots at the Agents endpoint, which is
  fully sufficient for per-request tracing and all cost/latency numbers. SPA-rooted traces are a
  stretch goal, not v1.

## Recommended wiring plan

**Item 1 — Tracing spine + dashboard (the demo-able slice).** Add the three OTel packages to
`CvManager.Agents` and `CvManager.Mcp`; register tracing+metrics with OTLP export subscribing to
`Experimental.Microsoft.Extensions.AI`, `Experimental.Microsoft.Agents.AI`,
`Microsoft.Agents.AI.Workflows`, `Experimental.ModelContextProtocol`, `System.Net.Http`, `Npgsql`
+ ASP.NET Core instrumentation; wrap the default and keyed chat clients with `UseOpenTelemetry`
in `GitHubModelsServiceCollectionExtensions`; add `.WithOpenTelemetry()` to the staffing
`WorkflowBuilder`; add the Aspire dashboard to `docker-compose.yml` and `OTEL_EXPORTER_OTLP_*`
env to both services. Sensitive data stays off. Acceptance: one staffing run renders as a single
trace — request → `workflow_invoke` → `executor.process` per step → `chat`/`invoke_agent` → MCP
RPC → SQL — and the metrics page shows `gen_ai.client.token.usage` by model. Small ticket; all
seams are one-file changes.

**Item 2 — Usage ledger enrichment (the tiering dataset).** A `MeteringChatClient`
(`DelegatingChatClient`) in the same builder chain capturing `response.ModelId`, latency, and
`Activity.Current.TraceId`; an ambient step tag set by the run services/pipeline steps; migration
adding `LatencyMs`, `TraceId`, `Step` to `AgentUsage`; fix `UsageMeter` to prefer the captured
model id over config (also unblocks correct labeling through the Gemini migration). Optional:
surface latency in the Usage tab. Independent of Item 1 but strictly better after it (trace ids
become clickable evidence).

## Feeds model tiering

The tiering decision (which agents/steps drop to a cheaper model) needs exactly three numbers per
**(step, model)** pair, and this wiring produces all of them: **tokens** (`AgentUsage` rows +
`gen_ai.client.token.usage` by `gen_ai.request.model`) → $/step from the Gemini price table;
**latency** (`gen_ai.client.operation.duration` histogram + `LatencyMs` column) → whether a
cheaper/slower model fits the SSE budget, and how much of a staffing run is model time vs. MCP/DB
time (`executor.process` vs. `chat` span durations); **failure shape** (`error.type` on chat
spans, `ExecutorFailedEvent` steps) → whether a candidate tier degrades more often. Item 2's
step column is the piece today's per-user-only ledger is missing.

## Sources

- `Microsoft.Agents.AI` / `.Workflows` 1.10.0 and `Microsoft.Extensions.AI` 10.7.0 nupkg XML docs
  + DLL string tables, local NuGet cache (**primary**: source/span/attr/metric names, option
  defaults, env-var behavior)
- [MAF observability guide](https://learn.microsoft.com/en-us/agent-framework/agents/observability) ·
  [.NET sample AgentOpenTelemetry](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/02-agents/AgentOpenTelemetry)
- [OpenTelemetryChatClient API docs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.opentelemetrychatclient) ·
  [source](https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI/ChatCompletion/OpenTelemetryChatClient.cs) ·
  [OTel GenAI semconv](https://opentelemetry.io/docs/specs/semconv/gen-ai/)
- [MCP C# SDK Diagnostics.cs](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/src/ModelContextProtocol.Core/Diagnostics.cs) +
  `ModelContextProtocol.Core` 1.4.0 DLL strings
- [Npgsql tracing](https://www.npgsql.org/doc/diagnostics/tracing.html) ·
  [metrics](https://www.npgsql.org/doc/diagnostics/metrics.html) + Npgsql 10.0.3 DLL strings
- [.NET networking tracing](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/telemetry/tracing) ·
  [built-in activities](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-builtin-activities)
- [Aspire dashboard standalone](https://aspire.dev/dashboard/standalone/) ·
  [Jaeger getting started](https://www.jaegertracing.io/docs/latest/getting-started/) ·
  [OTel JS fetch instrumentation](https://github.com/open-telemetry/opentelemetry-js/tree/main/experimental/packages/opentelemetry-instrumentation-fetch)
