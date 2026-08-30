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

### Roster editing

**Child Collection Replace**:
The write shape of an Experience: its achievements and its skill links travel inside the experience
payload, and a save replaces both lists wholesale rather than diffing them. So an editor for one is
a nested-collection form, not three resources — a bullet removed in the form is a bullet gone on
save, and nothing is written until the save. Contrast the child resources with their own endpoints
(languages, qualifications, availability, employee skills), each addressed and saved on its own.
_Avoid_: nested update, cascade save

**Fixed Catalog Link**:
The `SkillId` on an employee-skill row: set when the row is added and never reassigned afterwards.
`PUT /api/employee-skills/{id}` validates it and then writes the level and the years only, so
pointing a row at a different catalog skill is a remove and an add. The edit form shows the skill
disabled rather than editable — a control that appears to work and changes nothing is worse than one
that plainly cannot.
_Avoid_: immutable skill, read-only field

**Bullet Order**:
An achievement's position on the CV, carried as `Order` on the wire but never typed by a user: the
edit form assigns it from the bullet's index in the list at save time. Moving a bullet is the whole
interaction; the number is a consequence.
_Avoid_: sort key, priority

### SPA data layer

**One Import Site**:
The rule that every SPA component reaches the backends through exactly one import path
(`src/api`) and no component knows a URL, a query key, or which of the two hosts serves a call.
It is a property of the *public face*, not of the file count: `src/api/index.ts` is a barrel over
eighteen per-domain modules, and the rule holds because components import the barrel. A split that
made components import `api/agents/shortlist` directly would keep the modules and lose the rule.
_Avoid_: single api file, api facade

### Testing

**Boundary Test**:
A test that drives a host over HTTP with the app's own authentication in force — the only place the
service boundary's rules (the authorization fallback policy, the ProblemDetails mapping, route
constraints) are actually observable. Distinct from an Application-layer unit test, which reaches
past the boundary by construction.
_Avoid_: API test, controller test

**Virtual Authenticator**:
The CDP-installed software passkey that lets a headless browser complete a WebAuthn ceremony with
no user gesture — the only way an e2e test gets past the sign-in gate. Holds its credentials in the
browser context, so one test can register and then sign back in with the same passkey.
_Avoid_: fake passkey, mock authenticator (nothing is mocked — the real ceremony runs)

**Database Truth**:
Behaviour enforced by Postgres rather than by code — partial unique indexes, cascade deletes,
date/enum mapping. Invisible to EF InMemory, so it is only ever proven by an integration test
against a real database.
_Avoid_: DB constraint (too narrow), infrastructure behaviour

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

### Cost & budgets

**Turn Amplification**:
The reason a tool-looping agent's cost is not the sum of its payloads: every model call re-sends
the whole conversation, so a tool result costs its own size times the number of calls that follow
it. A large result fetched early is the expensive one; the same result fetched last is nearly free.
_Avoid_: context growth, token bloat

**Baseline Prompt Size**:
What one model call costs before any tool result — the agent's instructions plus the schemas of
every tool it is shown. Paid on each iteration, so it is multiplied by Turn Amplification.
_Avoid_: system prompt size, overhead

**Runtime Budget**:
The per-run ceiling — cumulative input tokens *and* model calls — an agent may spend before it must
answer from what it already holds, with a Degradation note. Two numbers because a token ceiling
cannot see a long loop of individually tiny calls. A safety net sized well above the expected cost —
crossing it is an incident, staying under it is unremarkable.
_Avoid_: token cap, quota (that is the per-user limit, a different thing)

**Closing Turn**:
What a run gets instead of a truncation once its Runtime Budget is spent: tools are withdrawn
(`ToolMode = None`) and the model is asked for its answer from the evidence already in hand. The
work already paid for is kept, and the answer is the model's own rather than a salvaged fragment.
_Avoid_: abort, cutoff, truncate

**Cost Floor**:
A committed per-call token ceiling, sibling to the Cluster Floor: a cost regression fails the
suite the way an accuracy regression does. Distinct from the Runtime Budget — the budget bounds
the worst case at runtime, the floor makes drift visible in CI.
_Avoid_: token budget, performance test

**Ratchet**:
A committed ceiling that may only ever move down: landed at the currently measured value, then
tightened by each change that improves it. Lets a floor guard a number that is still wrong,
without a red main.
_Avoid_: baseline (overloaded), threshold

**Tool Allowlist**:
The subset of the MCP read surface one agent identity is shown. Narrows Baseline Prompt Size and
the choices the model can thrash between. Distinct from MCP scopes, which gate read against write;
the allowlist gates which read tools.
_Avoid_: tool filter, tool subset

**Convergence**:
How few model calls an agent needs to reach its answer. Independent of cost: a converged run can
still be expensive, and a cheap run can still thrash.
_Avoid_: efficiency, loop length

**Convergent Path**:
The declared tool sequence a converged run of a named question makes, in order. Committed next to
the Cost Floors so the whole run can be priced without a model — a longer path is a red test, not
a bill — and so the shape of a run is reviewable in a diff.
_Avoid_: happy path, expected trace

**Tool Sequence**:
The ordered list of tools one agent run actually called, recorded on its usage row next to the
iteration count. Turns "this call cost 146,647 tokens" into a diagnosable row: the expensive
payload is named, not guessed at.
_Avoid_: tool trace, call log (those are the OTel spans, which are in-memory only)

**Structural Path Length**:
How many tool calls a task takes because of the shape of the tool surface, before the model does
anything wrong. Ingestion writes one child per call, so an ordinary two-role resume is sixteen
calls with no thrash in it at all. The counterpart to Convergence: Convergence is calls the model
could have avoided, this is calls it could not. A Runtime Budget set below it degrades every run.
_Avoid_: iteration count, loop length (those are what a run did, not what it needed)

### Agent dock

**Agent Surface**:
One place in the agent dock where a person does agent work — Roster Q&A, Tailor CV, Match,
Interview kit, Shortlist, Staffing, Roster scan, Bench report, Resume ingest. Nine of them, and the
picker names them in full because it shows one label at a time rather than dividing the panel by
their count. Surfaces are grouped by what they act on (the roster, one person, a role, the system),
which is the structure the flat tab strip was hiding.
_Avoid_: tab, mode

**Token Ledger**:
The Usage view — the user's spend against their caps, plus the per-agent breakdown. Deliberately
not an Agent Surface: it spends nothing and does nothing, so it lives in the dock header as a peek
you open and close, not as a place among the things that bill you.
_Avoid_: usage tab, quota screen

### Client registration

**Registration Ceiling**:
The set of client scopes a dynamically registered MCP client may hold — `mcp:read` plus the
audience mapper, and nothing above it. Declared on the realm's Allowed Client Scopes policy, so a
client cannot register its way to write capability. Distinct from a Tool Allowlist, which narrows
what an already-entitled identity is *shown*; the ceiling bounds what an identity may be
*entitled to* in the first place.
_Avoid_: registration allowlist, DCR scopes

**Stamped Rule**:
A client policy executor that runs with `auto-configure` and writes its rule onto the client
record at registration, instead of checking it at request time. Holds afterwards without a runtime
policy — which is what keeps the OAuth 2.1 baseline off the realm's own imported clients, whose
grants a runtime rule would break.
_Avoid_: augment, auto-config
