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
A per-cluster first-tool floor in the Tool-Selection Eval gate — the sharp instrument: a careless
edit to one description trips its own cluster long before it moves the overall average. Only
clusters measured at their floor twice are gated.
_Avoid_: per-group threshold

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
