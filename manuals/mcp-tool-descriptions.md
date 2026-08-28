# MCP tool reliability: the description bar and the sequencing audit

The tool-reliability bundle's written record (P1T-112). Part 1 is the **description bar** and
pass 1's measured before/after (P1T-128; pass 2 — CRUD/write tools — is P1T-129). Part 2 is the
**sequencing audit** (P1T-131).

## Part 1 — the description bar (P1T-128)

### Why a bar

Anthropic's tool-use guidance is blunt about it: *"Provide extremely detailed descriptions. This
is by far the most important factor in tool performance"* — what the tool does, when to use it
**and when not**, what each parameter means, what the tool does **not** return, 3–4+ sentences
([define-tools § best practices](https://platform.claude.com/docs/en/agents-and-tools/tool-use/define-tools),
digested in `manuals/anthropic-gemini-ichatclient-mapping.md`, P1T-106). Before this pass most of
our 40+ tools carried a single one-line sentence — `cv_get` was *"Assemble and return an
employee's full CV (all sections) by id. Returns data, not a PDF."* — and the tool-selection eval
(P1T-127) measured what that costs.

### The five parts every description carries

1. **What it does** — the shape of the result, in the caller's vocabulary, with the fields named.
2. **When to use it** — the question it answers, quoted the way a user actually phrases it.
3. **When NOT to use it, naming the sibling** — every confusable neighbour by tool name. This is
   the part that moves the eval; a tool that only praises itself competes with its own family.
4. **Input format notes with an inline example** — `Input: … e.g. {"page": 2, "pageSize": 50}`.
   There is no `input_examples` field in M.E.AI or MCP (Anthropic's API has one; the mapping doc
   records the gap), so examples live in the description text or nowhere.
5. **What it does NOT return** — the negative space: no PDF, no relevance scores, no employee
   data, no skill levels. Cheap to write, and it stops a wrong-tool call one sentence earlier.

`roster_digest_list` (P1T-121) was written to this shape first and is the reference
implementation; `tests/Mcp.Tests/ToolDescriptionBarTests.cs` pins the bar per tool so a tidy-up
cannot quietly delete a sibling cross-link.

### What pass 1 rewrote

The confusable READ clusters the P1T-112 audit named:

| Cluster | Tools | Disambiguation added |
| --- | --- | --- |
| roster reads | `employee_list`, `employee_get`, `cv_get` | each points capability questions at `roster_semantic_search`, JD ranking at `roster_shortlist_search`, bulk sweeps at `roster_digest_list`, and each other for one-person vs whole-roster; `cv_get` now advertises that its `achievementId`s are `style_exemplar_search`'s input |
| search trio | `roster_semantic_search`, `roster_shortlist_search`, `style_exemplar_search` | one free-form question vs a JD's 3–8 requirements vs phrasing exemplars — each names the other two, plus the digest tool for sweeps and the structured reads for exact facts |
| catalog reads | `category_list`, `category_tree`, `skill_list` | flat ids vs nested hierarchy (whose nodes carry their skills) vs the flat skill list; all three send per-person skills to `employee_get`, and `skill_list` separates `skill_create` (new catalog entry) from `employee_skill_add` (attach existing) |

Write/CRUD tools are deliberately untouched — that is P1T-129.

### Measured before/after (P1T-127 instrument)

`gemini-3.5-flash-lite`, 39 frozen golden prompts, real in-process MCP listing, `ToolMode.Auto`,
first-tool credit. Two runs before the rewrite, four after — all six with 0 transport errors.

| Cluster | pre-pass (×2) | post-pass (×4) | note |
| --- | ---: | ---: | --- |
| capability | 100% | 100% ×4 | held |
| exact-fact | 100% | 100% ×4 | held |
| bulk-sweep | 100% | 100% ×4 | held |
| catalog | 100% | 100% ×4 | held |
| shortlist | 75% | 100%, 100%, 75%, 75% | moved, but unstable — see below |
| style | 0% | 0% ×4 | unmoved — not a wording problem, see below |
| writes | 75% | 75% ×4 | P1T-129's scope |
| **overall first-tool** | **0.821, 0.821** | **0.846, 0.846, 0.821, 0.821** | any-call identical throughout |

**The pre-pass baseline is the one P1T-127 could not take** — its free-tier RPD ran out
mid-measurement, and the provisional floors it committed (0.85) were guesses that sat *above* the
real baseline. Measured 2026-08-28: 0.821, twice, with identical misses.

**Selection on this model is not deterministic, and the two identical pre-pass runs were a
coincidence.** Exactly one prompt moved: `sl-jd-paste` ("here is a job description with several
must-haves — find the top candidates with per-requirement evidence"). Pre-pass it chose
`employee_list` both times. Post-pass, across four runs, it never chose `employee_list` again — it
chose `roster_shortlist_search` twice (correct) and `roster_digest_list` twice (a miss). So the
durable, repeated result is *narrower* than the headline: the JD prompt left the plain roster
listing for the roster-search family, and which member of that family it picks is not pinned. One
prompt is 2.6 points of a 39-prompt average, so read the overall figures as 0.82–0.85 with a
0.821 floor case, not as a clean 0.821 → 0.846 gain.

The four gated read clusters (capability, exact-fact, bulk-sweep, catalog) sat at 100% in all six
runs — the rewrite cost nothing there, which is the other half of what the pass had to show.

That is also how the floors are now set — one prompt below the **minimum** observed, never from a
run pair that happened to agree: global 0.80 (min observed 0.821 = 32/39, so 31/39 trips it), and
**per-cluster first-tool floors** at the lowest figure each cluster held across every run
(capability / exact-fact / bulk-sweep / catalog 100%, shortlist and writes 75%). Cluster floors are
the sharper instrument: a careless edit to one description trips its own cluster long before it
moves the average. style is ungated on purpose — a floor nobody can hold teaches nothing. The
committed gate was then verified live end to end: run 4 came in at 0.821 with every floor held,
green.

### The style cluster: an affordance problem, not a wording one

Three prompts ask for phrasing help without naming a bullet ("show me examples of strongly phrased
achievement bullets about cost reduction"). `style_exemplar_search` has a **required**
`achievementIds` argument, so the model — told to start with a tool call and to use only the ids
the user gave — deflects to something it can actually fill: `roster_semantic_search`, `cv_get`, or
`roster_digest_list`. Two description edits were tried and measured (a phrasing-first "when NOT"
clause on `roster_semantic_search`, an explicit "this is the phrasing tool" line on
`style_exemplar_search`); neither moved the cluster off 0% in any of the three post-pass runs.
Descriptions cannot fix a tool the model has no legal way to call, so the fix is one of:

- make `achievementIds` optional with a free-text query fallback (a product change to
  `IExemplarSearchService` — exemplars for a described theme, not only for a specific bullet);
- or accept the prompts as measuring an unreachable state and re-label them (frozen set — a
  deliberate re-label only, with the before/after re-baselined).

Recorded here rather than guessed at: the honest read is that the golden set caught a real gap in
the tool surface, which is exactly what it is for. Tracked as P1T-136 — the choice between the two
options is a human's, since one changes the product surface and the other changes the yardstick.

## Part 2 — the sequencing audit (P1T-131)

### The rule

**Fixed order → code. Dynamic order → prompt + error-driven retry.**

If a tool call is a *fixed prerequisite* — it always happens, with arguments fully known before
the model says a word — hoping the tool loop performs it buys nothing and risks a skipped call,
a malformed argument, or a wasted round-trip. Invoke it in code (the P1T-117 shortlist-retrieval
pattern) and hand the captured result to the model. If the call is *genuinely dynamic* — the
model decides whether/what/with-which-arguments from mid-run reasoning — it stays model-driven,
steered by the prompt, with structured tool errors driving self-correction and (where grounding
is mandatory) `RequireAny` forcing plus the Capture-Verify Guard (P1T-130).

### The audit

| Agent | Call | Fixed or dynamic? | Where it landed |
| --- | --- | --- | --- |
| Shortlist | `jd-extraction` (tool-less model call) | fixed | code — `ShortlistRunService` orchestrates it (P1T-117) |
| Shortlist | `roster_shortlist_search` | fixed (arguments = the extractor's requirements + the request filters) | code — `IShortlistSearch` invokes the MCP tool directly (P1T-117) |
| Shortlist | rationale model call | n/a (tool-less) | code-orchestrated, model writes prose only |
| CV Tailoring | `cv_get` | **fixed** (the employee id arrives in the request) | **converted to code in P1T-131**: pre-fetched deterministically; the verbatim result opens the session; the model's tool surface shrank to the exemplar tool alone |
| CV Tailoring | `style_exemplar_search` | dynamic (the model picks which selected bullets deserve exemplars and passes their ids) | stays model-driven; per-run capture decorator records the selection + payload for the fabrication guard |
| Match | `cv_get` | fixed in principle | **stays model-driven, recorded**: the run is a single turn whose tool surface is already narrowed to `cv_get` alone and whose outcome is schema-constrained — the miss rate the conversion would buy down has not been observed since the narrowing. Converting would thread `employeeId` through `MatchAgent`/`MatchRunService` and re-script the match/staffing test fixtures — a materially larger change than Tailoring's for no demonstrated failure. Revisit if the live smokes or the tool-selection eval ever show a skipped `cv_get`. |
| Interview Kit | `cv_get` | fixed in principle | **stays model-driven, recorded**: same judgment as Match — tool surface already `cv_get`-only, 2-turn flow with composer-side evidence vetting against the captured result; conversion touches the evidence-vetting seam for no observed miss. Same revisit trigger. |
| Roster Q&A | `roster_semantic_search` / structured reads | dynamic (the question decides the tool) | prompt-driven selection + `RequireAny` on the first call + Capture-Verify Guard (P1T-130) |
| Resume Ingestion | staged `employee_create_draft` → child adds | dynamic chain (the model self-corrects off MCP's structured validation errors) | stays prompt-procedural **by design** — the error-driven retry loop is the mechanism, not a gap (P1T-92) |
| Roster Scan | `roster_digest_list` + scoring calls | fixed (the job enumerates the roster) | code — the scan runner drives everything; the model only scores (P1T-124/125) |
| Bench Report | `employee_list` | fixed | code — server-composed stats; the model writes prose over them (P1T-104) |

Summary: every fixed call in the system is now code-driven; the calls that remain model-driven
are either genuinely dynamic (Tailoring's exemplar search, Roster Q&A's tool choice, Ingestion's
correction chain) or fixed-in-principle with an honestly-recorded cost judgment (Match and
Interview Kit's `cv_get`, both already single-tool surfaces with structured outcomes).

### What the Tailoring conversion changed (P1T-131)

- `CvTailoringAgent.TailorAsync(Guid employeeId, string jobDescription, …)` — typed inputs
  replace the composed prompt; the agent invokes `cv_get` directly (`InvokeAsync` with
  `{ employeeId }`), captures the payload for the composer's evidence vetting exactly as before,
  and opens the 2-turn session with the JD + the verbatim tool result.
- The model's tool list is now `style_exemplar_search` only — no `cv_get` round-trip to hope for,
  one fewer way to go wrong, and a smaller prompt surface.
- A missing `cv_get` tool on the MCP listing is an upstream fault (502), same as the shortlist's
  missing-tool rule. A not-found employee flows through as the tool's error payload — the model
  says so plainly, as before.
- The composer, fabrication guard, and endpoint contract are unchanged.
