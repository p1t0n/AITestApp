# Handoff packages: structured context between agent steps and to humans

The knowledge-item record for the handoff bundle (P1T-132…135): how the staffing pipeline
accumulates one **HandoffPackage** per run, persists it on the proposal, and serves it to the
approver — so the next consumer (another stage, or a human arriving hours later) can trust the run
**without re-running anything**.

## Why a package at all

A pipeline run produces three different kinds of state, and losing any of them breaks a different
consumer:

| Field class | What it is | Who needs it |
| --- | --- | --- |
| **Context (inputs)** | What was asked: the JD and every filter, exactly as clamped/normalized | Anyone judging whether the run answered the right question |
| **Findings (report)** | The full StaffingReport: per-requirement evidence, complete match markdown, recommendation narrative, the extraction, notes | The approver — the findings ARE the decision material |
| **Authorization state (provenance)** | Who ran it, which agent identities acted under which scopes, on what model, at what token cost, under which caps | Anyone auditing what the run was *allowed* to touch |

Plus the honesty layer: **degradations** — what the run lost and why, mirroring the report's notes
in structured form (`{stage, whatWasLost, why}`), so partial results are legible without parsing
prose.

## The envelope (`api/Agents/Handoff/`)

```
HandoffPackage {
  inputs:        { jobDescription, availableOn, skillIds, location, minYears, matchTop }  // strings, nulls kept
  provenance:    RunProvenance { callerUserId?, capsSnapshotAtStart[], startedAt }
  slices:        StageSlice[]           // one per unit of agent work
  degradations:  DegradationEntry[]     // { stage, whatWasLost, why }
}

StageSlice {
  stage            // jd-extraction | shortlist | match | narrative
  agentClientId?   // the OAuth client id the stage authenticated as; null = tool-less stage
  scopes[]         // e.g. ["mcp:read", "mcp:search"] — what the identity could touch
  modelId?         // from the reply, when the model reported one
  inputTokens, outputTokens
  startedAt, completedAt   // from the injected TimeProvider
  status           // completed | failed | skipped
  degradeReason?   // why failed/skipped
  retryCount?      // 429 retries performed (match runs)
}
```

The envelope is deliberately **staffing-agnostic in shape** (no staffing types inside it) but its
generalization to other pipelines is consciously out of scope — one honest, working instance
before an abstraction.

### Credentials never travel — by construction

Authorization state is **provenance, not credentials**. The types have no field that could hold a
token, secret, or header. The identity facts come from `ConfigAgentIdentitySource`, which reads
only `ClientId` and `Scope` from the same `McpAuth:<agent>` config sections that register the
token providers — the `ClientSecret` key is never read, so it cannot leak into a package even by
future refactoring of the source. Two tests pin this:

- serialized-package key scan: no `secret`/`authorization`/`apikey`/`credential`/`password`/`bearer`
  keys anywhere; the only `*token*` keys are the `inputTokens`/`outputTokens` counters
  (`StaffingHandoffPackageTests`);
- the drill-in endpoint serves the same types, so the wire inherits the guarantee.

## Accumulation mechanics (P1T-132)

The pipeline's per-run `Runner` holds the accumulating lists (same lock as the progress events —
the match fan-out races). Each stage appends its slice where it already meters:

- **prepare** — no slice; it captures the package's opening facts: the inputs dictionary and the
  provenance (caller + caps snapshot). The caps snapshot is **fail-open**: an unreadable usage
  store yields an empty snapshot, never a failed run (consistent with the caps' own rule).
- **jd-extraction** — a slice whenever the extraction reply rides the shortlist run outcome; it
  shares the shortlist stage's time window (it runs inside it) and carries its own tokens.
  Tool-less → `agentClientId: null`, `scopes: []`, honestly.
- **shortlist** — completed slice with tokens/model from the reply. A soft fault (no response) or
  transport fault appends a **failed** slice — a fault that spent tokens still reports them — plus
  a degradation entry ("The entire staffing report").
- **match ×N** — one slice per candidate run, `retryCount` counted by an `onRetry` hook inside the
  existing 429-retry loop. A terminal failure → failed slice + degradation entry per candidate.
  A cap trip before the fan-out → **skipped** slices (one per would-be run) + one degradation
  entry whose `why` equals the report's cap note verbatim.
- **narrative** — completed slice with tokens; unparseable output → failed slice that still
  reports the tokens spent; a dropped recommendation → completed slice + degradation entry;
  cap trip → skipped slice.

**OTel**: each slice's facts are stamped as `handoff.slice.*` tags on the existing stage spans
(`Activity.Current` at append time) — no new span hierarchy.

The package rides `StaffingRunOutcome.Package`; the report/SSE contracts are unchanged.

## Persistence + restart survival (P1T-133)

`StaffingProposal.PackageJson` — `jsonb` on PostgreSQL (provider-guarded; plain text on other
providers), nullable for pre-migration rows. `StaffingProposalStore.CreateAsync` serializes a
`StaffingHandoffDocument {inputs, report, provenance, slices, degradations}` — camelCase, same
spelling as the wire — with the **full report, no truncation**: the report is the findings.

Two fidelity rules:

- the persisted report is stamped with its own `proposalId` at creation (the row id is generated
  before serialization), so the drill-in shows exactly what the requester's SSE report showed;
- creation stays **best-effort** — a persistence fault degrades the report's `proposalId` and adds
  a view-only note; it never fails a run that already succeeded.

`StaffingHandoffDocument.TryDeserialize` returns null on legacy/corrupt columns; readers degrade
to the snapshot columns, never throw. Restart survival is pinned by a fresh-context reload test.

## The drill-in + sufficiency gate (P1T-134/135)

`GET /agents/staffing/proposals/{id}` (identified users only — the decision endpoint's rule)
returns the inbox metadata plus the deserialized package; pre-package rows return `package: null`
explicitly. The widget's approval inbox (Staffing tab → "Pending proposals") drills in and renders
the package through the **same components the live run uses** — recommendation card, candidate
cards, extraction chips — plus a compact provenance line and the degradations as amber notes.
There are no re-run buttons: the approver decides from the package alone.

**The sufficiency gate** makes "the package is enough" a permanent property, not a hope: an
end-to-end test runs the pipeline through the faked host, fetches the drill-in, and walks every
public `StaffingReport` property by reflection (every optional populated, so `WhenWritingNull`
can't hide one), requiring its camelCase key in the `package.report` JSON node. A report field
added later that doesn't reach the approver fails the suite. A store-level twin
(`The_persisted_report_carries_every_wire_report_field`) guards the persistence side.

## Where things live

| Concern | Location |
| --- | --- |
| Envelope types | `api/Agents/Handoff/HandoffPackage.cs` |
| Identity source (ClientId+Scope only) | `api/Agents/Handoff/AgentIdentitySource.cs` |
| Accumulation | `api/Agents/Staffing/StaffingPipeline.cs` (Runner) |
| Persisted document | `api/Agents/Staffing/StaffingHandoffDocument.cs` |
| Column + migration | `StaffingProposal.PackageJson`, `ProposalHandoffPackage` migration |
| Drill-in endpoint | `GET /agents/staffing/proposals/{id}` (`api/Agents/Program.cs`) |
| Inbox + drill-in UI | `web/src/components/agent/ProposalInbox.tsx` |
| Tests | `StaffingHandoffPackageTests`, `StaffingProposalStoreTests`, `StaffingProposalDrillInTests`, `AgentWidget.proposals.test.tsx` |
