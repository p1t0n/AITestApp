# MAF orchestration primitives in Microsoft.Agents.AI 1.10 (research, P1T-70)

The research that grounded the staffing pipeline design (wayfinder map P1T-69): what does
Microsoft Agent Framework actually give us for multi-agent orchestration in the packages we can
realistically run? Findings were verified against the shipped `Microsoft.Agents.AI.Workflows`
1.10.0 package XML docs plus the official docs; the verdict here is what
[`staffing-pipeline.md`](staffing-pipeline.md) implements.

**Headline: orchestration lives in a separate, GA package — `Microsoft.Agents.AI.Workflows` — and
version 1.10.0 exists, exactly aligned with our pins (`Microsoft.Agents.AI` 1.10.0, M.E.AI ≥10.6,
net10.0). Adding it required zero upgrades. Nothing we need is version-gated** (latest at research
time: 1.13).

## What 1.10.0 ships (package-verified)

| Area | What exists |
|---|---|
| Graph model | `WorkflowBuilder`: `AddEdge`, **`AddFanOutEdge`**, **`AddFanInBarrierEdge`**, `SwitchBuilder` (conditional routing), `WithOutputFrom`; superstep execution |
| High-level builders | `Sequential-`, `Concurrent-` (fan-out → accumulate → fan-in aggregate), `Handoff-`, `GroupChat-`, `MagenticWorkflowBuilder` — all speak the chat-message protocol |
| Agents as steps | `AIAgentBinding` — existing `ChatClientAgent`s plug in directly |
| **Plain code as steps** | `FunctionExecutor<TIn,TOut>` / `Executor<TIn,TOut>` / `AggregatingExecutor` — our composers, guards, and capture seams fit natively *between* agent steps, typed |
| Streaming/progress | `StreamingRun` event stream: `ExecutorInvoked/Completed/FailedEvent`, `AgentResponseUpdateEvent` (token streaming), `SuperStep*`, `WorkflowOutput/ErrorEvent` — per-step UI progress is first-class |
| Errors | Failures are events (`ExecutorFailedEvent` carries executor id + exception); partial-result policy is the application's to implement in the fan-in aggregator |
| Checkpointing | Auto per-superstep; `CheckpointManager` + `InMemory`/`FileSystemJsonCheckpointStore`; `WorkflowSession` resume. A separate durable/distributed runtime exists — overkill for us |
| HITL | `RequestPort<TReq,TResp>` — not needed for the pipeline; relevant to a future router map |
| Extras | OpenTelemetry built in; `WorkflowVisualizer` |

## The one gap that matters

**No max-concurrency knob on fan-out** — `AddFanOutEdge` broadcasts to all targets in one
superstep. With GitHub Models rate limits (already bitten during eval runs), **throttling is the
application's job**: a rate-limiting gate inside the match step or a throttling `IChatClient`
decorator. This was a design point for the architecture ticket (P1T-72), not a blocker.

## Verdict

| Verdict | Items |
|---|---|
| **Use** | `Microsoft.Agents.AI.Workflows` 1.10.0; explicit `WorkflowBuilder` graph (or Sequential+Concurrent builders); `FunctionExecutor` seams; `StreamingRun` for progress; event-driven degraded aggregation |
| **Skip** | Handoff/GroupChat/Magentic builders (router-map material — they orchestrate via the chat protocol, and our steps exchange typed outcomes); durable/distributed runtime; `RequestPort` HITL |
| **Version-blocked** | Nothing |
| **We must build** | Fan-out throttling/backoff; partial-failure policy in the aggregator; metering/caps per step |

## What P1T-75 actually did with this

The shipped pipeline (`api/Agents/Staffing/StaffingPipeline.cs`) follows the verdict with one
deliberate narrowing and one wrinkle discovered in the package:

- **Spine, not graph fan-out**: an explicit `WorkflowBuilder` chain of `FunctionExecutor`s, with
  the match fan-out as a bounded-parallel task group *inside* the match executor instead of
  `AddFanOutEdge`/`AddFanInBarrierEdge` — N is dynamic (the barrier expects a message per
  build-time target) and the shared throttle (default 2) makes graph-level fan-out cosmetic.
  Rationale documented on the class and in [`staffing-pipeline.md`](staffing-pipeline.md).
- **`ExecutorOptions` has no public constructor in 1.10.0**, so an executor can't declare its
  workflow outputs through options; the report sink instead passes `outputTypes:
  [typeof(ReportResult)]` to the `FunctionExecutor` constructor and yields the result explicitly
  with `context.YieldOutputAsync(...)`, and the builder marks it via `WithOutputFrom`.
- `StreamingRun` events were not needed for the UI in the end: the pipeline emits its own ordered,
  domain-shaped progress events (`StaffingProgressEvent`) that the SSE endpoint maps to the wire —
  the workflow's `WorkflowOutputEvent`/`WorkflowErrorEvent` are consumed only to extract the run's
  result.

## Sources

- [Workflows overview](https://learn.microsoft.com/en-us/agent-framework/workflows/) ·
  [core concepts](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/core-concepts/workflows) ·
  [checkpoints](https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints)
- [MAF 1.0 announcement (devblog)](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- `Microsoft.Agents.AI.Workflows` 1.10.0 nupkg XML-doc extraction (**primary**)
