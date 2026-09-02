# ExpertToJob

Manages available experts (skills, qualifications, experience, availability), renders their
CVs, and runs AI agents over that roster. This glossary pins the ubiquitous language; design
detail lives in `/manuals` and the Linear decision trail.

## Language

### The domain

**Expert**:
A person the system holds a CV for: their skills, qualifications, experience and availability.
The roster is a set of Experts, and every agent that reads the roster is reading Experts. Named
for what the product sells rather than for an employment relationship — an Expert need not be
employed by whoever runs the instance, and the word has to survive contractors and bench.
_Avoid_: employee, candidate (a candidate is an Expert in the context of one Job), resource

**Job**:
The work an Expert is being considered for, as described by the job description a Service Manager
brings in. The unit a Match, a Shortlist and a Proposal are all _about_: none of them mean
anything without the Job they were run against. Not yet a persisted entity — today a Job arrives
as JD text on the request and lives only for that run.
_Avoid_: position, vacancy, role (role means authorization here), requisition

**Service Manager**:
The staff user who selects Experts for a Job and holds the decision. The only actor who can
approve or reject a Proposal — agents propose, a Service Manager disposes — and the identity
behind the session token the Web host mints. The code still calls this `User`; the rename rides
with the role split (P1T-167) rather than with the Expert rename, so the term is pinned here
first and the type follows.
_Avoid_: staffer, recruiter, admin (admin is an MCP scope), approver (says the one act, not the role)

**Processing Record**:
One append-only row stating why the service may hold one Expert's data at one moment: the
**Origin** the row came from, the **Lawful Basis** that follows from it, the transparency-notice
version the person acknowledged, and when. A change appends a new record and leaves the previous
one exactly as written — the history is the artefact, because "this row was on legitimate interest
until March" has consequences (see `manuals/gdpr-processing-basis.md`).
_Avoid_: consent record (there is no consent here), audit log, basis flag

**Claim**:
One person's request to be recognised as the subject of one roster row. Raised when the address they
registered with matches a bench row, decided by a Service Manager, and **kept after it is decided** —
the history has to be able to say "rejected, then claimed again by somebody else". A claim grants
nothing while it waits: the claimant owns no row, which is indistinguishable from owning none at all.
_Avoid_: request, application (an application is for a Job), verification (nothing is verified here)

**Claim Code**:
A single-use secret a Service Manager generates for one roster row and hands over out of band — in
person or by phone, never by email. Redeeming it binds ownership with no approval step, because the
code *is* the proof: it is the only evidence this service can offer that is stronger than a matching
email address, and it exists precisely because email is never verified here.
_Avoid_: invite, token (a token is a session), magic link (there is no link and no mail)

**Paused**:
An Expert who has taken themselves off the bench: they stop being offered for work — no search, no
match, no scan reaches them — while their record and everything in it stays exactly as it was. Their
own act and nobody else's; a Service Manager who wants somebody off the bench deactivates the
account instead. Reversible and free: nothing is deleted, so nothing is re-embedded coming back.
_Avoid_: inactive (status means published-or-draft), disabled, deactivated (that is the account),
archived, soft-deleted (nothing is deleted)

**Personal-Data Declaration**:
The one code-level list of every store holding or pointing at a person, each classified
`delete | scrub | keep` with the reason written next to it. Erasure scrubs from it and the access
view reads it; a store carrying an ExpertId or a UserId that nobody declared fails the build. The
list is the artefact — two of them would drift, and the drift would be invisible until an audit.
_Avoid_: data map, inventory (an inventory is a document; this is code the tests execute)

**Erasure**:
Deleting a person: their account and their record together, hard, in one act, gated by the control
word. Irreversible and unannounced — there is no email on this service, so no confirmation link and
no way to reach somebody afterwards. Distinct from [Paused], which is reversible and free, and from
a scrub, which is what happens to rows a human already decided something on.
_Avoid_: deactivation (that is the account status), soft delete, anonymisation (the residue is
pseudonymous and restricted, not anonymous)

**Access View**:
Everything Art. 15 owes one person about their own record, in one place: what is held, why, who it
reaches — including the named model provider — how long it is kept, their rights, where the record
came from if they did not give it to us, and **what software concluded about them**. Distinct from
the Transparency Notice, which is a versioned artefact acknowledged at a moment in time; the access
view describes the service as it stands now.
_Avoid_: privacy page (that is the surface P1T-191 builds), data dump, profile

**Export**:
The machine-readable copy of what a person provided — their record and its basis history, and
deliberately none of the scores, bands, rationales or digests derived about them. Art. 20 owes it to
a 6(1)(b) record; a legitimate-interest record is offered the identical file, labelled a courtesy
rather than a right. A rendered PDF is a readability courtesy and never the portable artefact.
_Avoid_: download, backup, data dump (the exclusions are the point)

**Retention Clock**:
How long one record is kept and from when. Two of them: a claimed record runs two calendar years
from the person's **own** last activity, an unclaimed one six months from collection — shorter
precisely because nobody can be told it exists. Staff edits and agent scoring never move it; if
being looked at counted, a bench running weekly scans would keep everybody alive by looking at them.
_Avoid_: TTL, expiry policy (the expiry is the consequence, the clock is the rule), archive

**Contest**:
An Expert asking for a person to look at a score software gave them, saying why, and having the
outcome recorded on the row. The Art. 22(3) safeguard that our reliance on Art. 22(2)(a) obliges —
so it is a legal requirement, not a courtesy. Deliberately not an appeals workflow: no states, no
deadline, no escalation. A person can only contest a score they can see, which is why the access
view shows scores and rationales in full.
_Avoid_: appeal, dispute, complaint (a complaint goes to a supervisory authority), override

**Origin**:
How a roster row came to exist — `SelfRegistered` or `StaffCreated` — and the **only** input to
its Lawful Basis. Not a synonym for who owns the row: ownership says which Expert reaches it,
origin says why the service may hold it at all.
_Avoid_: source, provenance (provenance is a staffing-run term)

**Transparency Notice**:
The versioned text shown before registration saying what the service holds, who sees it, that AI
scores and ranks the person, how long it is kept, and their rights. Acknowledging it is required
to register, and the version acknowledged is recorded. **It is not a consent**: under Art. 6(1)(b)
necessity does the legal work, and a consent control where another basis applies is misleading.
_Avoid_: consent, terms, privacy policy (a policy is a document, this is a versioned artefact)

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
`expert_update`'s write semantics: only the fields present in the request change; an omitted or
null field keeps its current value. Complement to the full-replace `SaveExpertDto` path used by
`expert_create` and REST's `PUT`, which still requires every field on every call. An empty string
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
The render-ready dump of one expert in fixed order (`CvDto`), assembled by `CvService` as a pure
function of the expert detail. Every renderer — the SPA page, the server-side PDF — consumes this
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
(languages, qualifications, availability, expert skills), each addressed and saved on its own.
_Avoid_: nested update, cascade save

**Fixed Catalog Link**:
The `SkillId` on an expert-skill row: set when the row is added and never reassigned afterwards.
`PUT /api/expert-skills/{id}` validates it and then writes the level and the years only, so
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
A compact, deterministic career summary of one expert (the RAG narrative text), served over
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
the allowlist gates which read tools. Declared once in `CostFloors.AgentToolAllowlists`; the agent
config and the Keycloak realm are both asserted against that declaration.
_Avoid_: tool filter, tool subset

**Tool Grant**:
The Tool Allowlist as the token carries it: an `mcp:tool:<name>` scope on the agent's Keycloak
client, enforced by the MCP server at `tools/list` and `tools/call`. The boundary the allowlist
only described — a client can decline to show itself a tool, but only a grant can stop it calling
one. Grants compose with the capability scopes and only ever narrow them; a token carrying no
grant is narrowed by nothing.
_Avoid_: per-tool scope (says the mechanism, not the rule), tool permission

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
anything wrong. Ingestion writes one child per call, so an ordinary two-role resume is twenty-three
calls with no thrash in it at all. The counterpart to Convergence: Convergence is calls the model
could have avoided, this is calls it could not. Not the same as a run's cost — see Turn Batching,
which pays the same twenty-three calls over seven turns.
_Avoid_: iteration count, loop length (those are what a run did, not what it needed)

**Turn Batching**:
Issuing every tool call that does not need another's result together, as parallel calls in one
turn. What makes Structural Path Length survivable: Turn Amplification multiplies TURNS, not calls,
so on a write loop the turn boundary is the lever and the call count is nearly free. The reference
ingestion is twenty-three calls either way — one per turn costs 103,865 estimated tokens, batched
by kind costs 31,247. It is why P1T-155 could make a path eight calls LONGER (one filtered
`skill_list` lookup per skill in place of one catalog dump) and 72% cheaper at the same time.
_Avoid_: parallel tool calls (that is the mechanism), bulk endpoint (no such thing here — the
writes are unchanged, only their turn boundaries move)

### App shell

**Rail**:
The shell's left navigation edge — the app's own places (CVs, Skill Catalog, Users) plus the
session block. One of two edges that can cover the page; like the agent dock it publishes the width
it is covering and the shell makes room, so neither edge participates in layout.
_Avoid_: sidebar, nav bar, drawer

**Page Header**:
The one strip at the top of a routed page carrying its title, its way back, and its primary
actions. A page owns its contents; it does not own its heading.
_Avoid_: page title, toolbar

**CV Sheet**:
The rendered page of a CV Projection as a person sees it before printing. A client-facing artifact,
so it looks the same for every user regardless of theme — what is on screen is what prints.
_Avoid_: CV preview, print view

**Command Palette**:
The ⌘K surface that jumps to a place, a person, or an Agent Surface from one keystroke. It searches
the whole roster rather than a page of it, because the roster endpoint is unpaged — the same cached
response the roster table filters. Advertised by the rail's `Search` row; mounted beside the dock,
because it must open with no rail on screen and it acts on the dock as well as on the routes.
_Avoid_: quick search, spotlight, omnibox

**Surface Request**:
"Show this Agent Surface", sent to whichever dock is mounted. An event, not a value: delivered
synchronously, remembered by nobody, dropped when no dock is listening — so it adds no second answer
to which surface is showing, and no field to `AgentDock`. A name the dock does not recognise is
ignored rather than blanking the panel.
_Avoid_: surface state, dock navigation event

**Light Lock**:
The rule that the CV Sheet renders under the light theme in both Theme Modes, and the nested
`ThemeProvider` that enforces it. Named because it is a *lock*, not a default: no app-level mode may
reach the sheet, since the artifact leaves the building. The subtlety worth keeping is that the
provider only re-themes what names a palette role — the light text colour reaches the rest by being
set on the sheet element itself, which everything inside then inherits.
_Avoid_: print theme, light override, forced light mode

**Print Cascade**:
What a browser actually resolves at print media, as distinct from the `@media print` rules the app
emits. The distinction is the point: an emitted declaration can be attached to the right element and
still lose, so "the rule is there" and "the rule wins" are separate claims with separate evidence —
jsdom can only ever support the first. Anything claimed about a printed artifact is settled by
driving a real browser at the print media, and a rule is only correct if it also stays *out* of the
screen cascade.
_Avoid_: print styles, print CSS, media query

**Theme Mode**:
Light or dark, defaulting to the operating system's preference until a person overrides it. A
display preference of the browser, never of the account — it is not roster data and does not travel
with the user. What is stored is the override, so "no value" means "still following the OS" rather
than "unknown".
_Avoid_: dark mode setting, appearance profile

**App Rail**:
The app's left edge: the three places, the theme control, who is signed in, and the way out.
Collapsible to icons, a temporary drawer below `md`, and gone from a printed page. Like the Agent
Dock it publishes how much of the viewport it covers and takes no part in layout — the shell makes
room for whatever an edge says it is covering.
_Avoid_: sidebar, nav bar, drawer

**Content Floor**:
The narrowest the routed content between the two pushing edges is allowed to get. It is what
decides which edge yields: the rail gives up its labels rather than let the dock squeeze the
content past the floor, so the layout at any width is a stated rule rather than an accident.
_Avoid_: min width, breakpoint

**Design Token**:
A value the look is made of — a surface, a text colour, the accent, a radius — declared once and
then expressed through the UI library's own vocabulary. A component names the *role* it wants, never
the token: that is what makes a second Theme Mode cost a component nothing.
_Avoid_: theme variable, CSS var, palette entry

**Surface Ramp**:
The three steps anything can be drawn on: the page, a panel on it, and a well inside that panel.
Depth is a step on the ramp plus a hairline, not a shadow — a shadow separates nothing on a
near-black page.
_Avoid_: elevation, z-layer, background shades

**Well**:
The third step of the Surface Ramp, as a thing a component can ask for: a panel-inside-a-panel that
carries its own fill — a message bubble, a degradation note. A named Paper variant rather than three
`sx` declarations repeated, because it is neither outlined (a hairline on a coloured fill reads as a
defect) nor elevated (there is no elevation to speak of).
_Avoid_: card, tinted box, inner panel

**Overlay Shadow**:
The one shadow in the design system, and the only thing allowed to carry one: a surface that
genuinely floats over another — a menu, a dialog, an autocomplete popup, the undocked agent panel.
Everything merely *next to* something else separates with a hairline. Stated as a token per Theme
Mode, because a near-black page cannot be shadowed the way a light one can.
_Avoid_: elevation 8, drop shadow, box shadow

**Override Policy**:
The rule that decides where a look is written: needed twice, it belongs in the theme's component
overrides; needed once and about position or spacing, it stays in the component's own `sx`. What
keeps "the app's style" a thing that can be changed in one place instead of a convention 150 `sx`
blocks are each half-following.
_Avoid_: theme customisation, styling convention

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

**Dock Bar**:
The dock's one piece of chrome: what the panel *is* (the title and the three controls — the Token
Ledger peek, float/dock, close) on top of where it is *pointed* (the Agent Surface picker, or the
way back out of the ledger). Two rows, one surface, one hairline under the pair — a single bar, not
a header plus a strip. It carries no accent: the app's accent belongs to the primary action and the
focus ring, and a solid accent header was the largest thing in this app breaking that rule.
_Avoid_: dock header, title bar, toolbar

**Resize Handle**:
The dock's left edge as a control: a window splitter with a name, a width it announces, and arrow
keys, whose grip is drawn at rest and lights up on hover or keyboard focus. Named as a control
because that is the change — it existed as a mouse gesture with no visible presence, which is an
affordance only the people who already knew about it could use.
_Avoid_: drag strip, splitter bar, resizer

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
