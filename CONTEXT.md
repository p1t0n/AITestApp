# CV Manager

Manages available employees (skills, qualifications, experience, availability), renders their
CVs, and runs AI agents over that roster. This glossary pins the ubiquitous language; design
detail lives in `/manuals` and the Linear decision trail.

## Language

### Staffing & approval

**Proposal**:
A staffing run's pending recommendation awaiting exactly one human decision (approve/reject).
_Avoid_: approval request, recommendation record

**Handoff Package**:
The structured artifact that transfers accumulated context, findings, and authorization state
when control moves between pipeline steps or to a human operator. Neutral envelope; the staffing
pipeline is its first consumer, persisted on the Proposal.
_Avoid_: context blob, snapshot

**Stage Slice**:
One pipeline step's contribution to a Handoff Package: that stage's findings plus its provenance.
_Avoid_: step record

**Provenance**:
The auditable record of who/what acted in a run — caller identity, agent client, scopes, model,
token spend, caps state. Authorization state is recorded as Provenance; live credentials are
never persisted.
_Avoid_: audit trail, auth context

### Requirement extraction

**Requirement Extraction**:
The dedicated, tool-less structured call that distills a job description into JdRequirements —
the single source of requirements for Shortlist, Match, Interview Kit, and Roster Scan.
_Avoid_: JD parsing, requirement distillation

**Evidence Span**:
The verbatim job-description quote backing one extracted requirement, verified against the
source text.
_Avoid_: citation, snippet (that word belongs to search results)

**Inferred Requirement**:
An extracted requirement whose Evidence Span could not be verified verbatim — kept and badged,
never silently trusted or silently dropped.
_Avoid_: unverified requirement

**Capture-Verify Guard**:
The postcondition pattern for tool reliability: the agent's claim is checked against a captured
tool result after the fact — no capture behind the claim → retry once, then degrade with a note.
Complements (or substitutes for) forced tool invocation.
_Avoid_: tool enforcement, output validation (too generic)

### Tool reliability

**Description Bar**:
The five-part shape every MCP tool description must carry: what it does, when to use it, when NOT
to (naming the sibling tool), input notes with an inline example, and what it does not return.
Pinned by tests, measured by the Tool-Selection Eval.
_Avoid_: tool docs, tool prompt

**Tool-Selection Eval**:
The frozen golden-prompt instrument that measures which tool the model calls FIRST for a given
request, over the real MCP listing. Its gate is a set of measured floors, global and per cluster.
_Avoid_: tool test, selection benchmark

**Cluster Floor**:
A per-cluster first-tool floor in the Tool-Selection Eval gate — sharper than the aggregate: a
careless edit to one description trips its own cluster long before it moves the overall average.
Set from the minimum observed across at least three runs, minus headroom; a cluster nobody can
hold is left ungated.
_Avoid_: per-group threshold

**Partial Update**:
`employee_update`'s write semantics: only the fields present in the request change; an omitted or
null field keeps its current value. Complement to the full-replace `SaveEmployeeDto` path used by
`employee_create` and REST's `PUT`, which still requires every field on every call. An empty string
clears an optional field; null does not (P1T-137). Exists to remove a forced read-before-write, so
it is measured by the Tool-Selection Eval like any other affordance.
_Avoid_: patch, partial replace

### Style exemplars

**Theme Mode**:
`style_exemplar_search`'s id-less retrieval path: a free-text theme (e.g. "cost reduction") is
embedded directly and ranked against the whole achievement-bullet pool, for a phrasing request
that names no specific bullet. Mutually exclusive with id-keyed mode (`achievementIds`); exactly
one must be supplied.
_Avoid_: free-text search, keyword mode

**Themed Exemplars**:
The result of a Theme Mode search — a sibling to the id-keyed `BulletExemplars`, not a
nullable-keyed variant of it, so the response states honestly which mode produced it.
_Avoid_: theme result, generic exemplars

### CV rendering

**CV Projection**:
The render-ready dump of one employee in fixed order (`CvDto`), assembled by `CvService` as a pure
function of the employee detail. Every renderer — the SPA page, the server-side PDF — consumes this
same projection, so they cannot drift apart in content.
_Avoid_: CV model, CV view model

**Headless Render**:
Producing a CV document with no browser in the loop: `ICvPdfRenderer` turns a CV Projection into
PDF bytes from a pure, network-free call, so a worker, an export path or an agent can render one
without a print dialog or a Chromium process. The SPA's print button stays as the human-driven
alternative.
_Avoid_: PDF export, server print

### Roster scanning

**Roster Scan**:
Exhaustive, asynchronous scoring of the (optionally filtered) roster against one job
description. Complement to JD-only Match, which is top-K and synchronous.
_Avoid_: bulk match, batch scan

**Scoring Job**:
The persisted record of one Roster Scan run — per-candidate statuses, pause/resume state.
Survives restarts; pausable on quota or cap exhaustion.
_Avoid_: batch job

**Digest**:
A compact, deterministic career summary of one employee (the RAG narrative text), served over
MCP for scoring prompts.
_Avoid_: profile blob, CV summary

**Degradation**:
An explicit marker in a report or Handoff Package that a step failed or was skipped and what was
lost — absence is stated, never papered over.
_Avoid_: partial failure (as a field name)
