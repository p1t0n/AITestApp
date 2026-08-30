# MCP tool reliability: the description bar and the sequencing audit

The tool-reliability bundle's written record (P1T-112). Part 1 is the **description bar** and both
passes' measured before/after — pass 1 the read clusters (P1T-128), pass 2 the write surface
(P1T-129). Part 2 is the **sequencing audit** (P1T-131).

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

### The affordance pass 1 could not add: `skill_list`'s filter (P1T-145)

Pass 1 made `skill_list` say *when* to reach for it. It could not make the call cheap, because the
tool had no filter: resolving one skill name returned all 79 catalog rows, which the P1T-144
trace then measured at 42% of a 160,220-token roster-qa run. P1T-145 added `nameContains` plus
`page`/`pageSize`, and the description now leads with the filter and carries two inline examples
— `{"nameContains": "react"}` for a lookup, `{}` for the catalog — so the cheap call is the one
the model reads first.

Worth generalising from: **a description can only redirect a model between tools; it cannot make a
tool cost less.** Every description-pass finding of the form "the model called the right tool and
it was still expensive" is an affordance ticket, not a wording one. `style_exemplar_search`
(P1T-136) and the read-before-write pair (P1T-137) were the same shape.

The Tool-Selection Eval was **not** re-run for this change. Its floors were left where they are
(never loosened to accommodate a change) and the golden set left frozen; the re-baseline folds
into P1T-148, which re-runs it twice anyway. The prompt to add then is a skill-id lookup — "what
is the catalog id for React?" — which the pre-P1T-145 tool had no cheap answer to, so the set
never asked it.

### What pass 2 rewrote (P1T-129)

The remaining 28 one-liners — the whole write surface — plus `availability_list`, which sits in the
middle of the availability family:

| Family | Tools | Disambiguation added |
| --- | --- | --- |
| employee root | `employee_create`, `employee_create_draft`, `employee_update`, `employee_delete` | active-vs-draft (drafts are hidden until a human promotes them and may carry an empty email — the ingestion path); "capacity is not a root field, use `availability_add`"; children are separate calls after the id comes back |
| the person-vs-catalog trap | `employee_skill_add` ↔ `skill_create`, `employee_skill_update` ↔ `skill_update`, `employee_skill_delete` ↔ `skill_delete`, plus `experience_skill_add` | each side names the other explicitly: attaching an EXISTING catalog skill to a PERSON vs adding a NEW skill to the shared CATALOG (touching nobody) vs linking a skill to one ROLE as evidence |
| child families | `language_*`, `availability_*`, `experience_*`, `achievement_*`, `qualification_*`, `experience_skill_*` | which id each takes — the parent id to add, the row's own id to update or delete — since that is the other standing confusion; a certification is not a skill; a bullet belongs to a role, not a person |
| catalog structure | `category_create/update/delete` | category-vs-skill, and the cycle rule on re-parenting |

Parameter descriptions carry the formats that actually bite: dates as `yyyy-MM-dd`, the enum values
spelled out (`Beginner|Intermediate|Advanced|Expert`, `Basic|Conversational|Professional|Fluent|Native`,
`Degree|Certification`), which GUID is expected, and — where a DTO replaces a whole collection —
an explicit "full replace, include what should survive" warning. Every destructive tool announces
itself, names the non-destructive alternative (0% capacity instead of deleting a person; the
child's own delete instead of the parent's), and states that it needs the admin scope.

`employee_get` also gained a pointer at the write tools, because "change the title of employee X"
measurably landed there.

### Measured before/after (P1T-127 instrument)

`gemini-3.5-flash-lite`, 39 frozen golden prompts, real in-process MCP listing, `ToolMode.Auto`,
first-tool credit. Two runs before any rewrite, four after pass 1, four after pass 2 — all ten
with 0 transport errors.

| Cluster | pre-pass (×2) | after pass 1 (×4) | after pass 2 (×2) | note |
| --- | ---: | ---: | ---: | --- |
| capability | 100% | 100% ×4 | 100% ×2 | held throughout |
| exact-fact | 100% | 100% ×4 | 100% ×2 | held throughout |
| bulk-sweep | 100% | 100% ×4 | 100% ×2 | held throughout |
| catalog | 100% | 100% ×4 | 100% ×2 | held throughout |
| shortlist | 75% | 100%, 100%, 75%, 75% | 75%, 100%, 75%, 75% | left `employee_list`, but never settled |
| style | 0% | 0% ×4 | 0% ×4 | unmoved — an affordance problem, see below |
| writes | 75% | 75% ×4 | 83%, 83%, 75%, 75% | pass 2's gain: `write-experience` left `skill_list` for `experience_add` |
| **overall first-tool** | **0.821, 0.821** | **0.846, 0.846, 0.821, 0.821** | **0.846, 0.872, 0.821, 0.821** | any-call identical throughout |

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

### After the affordances (P1T-136 + P1T-137, measured 2026-08-29)

Same instrument, now under P1T-138's `Temperature = 0` pin, with both affordance fixes landed:
`style_exemplar_search`'s Theme Mode and `employee_update`'s Partial Update (plus `skill_create`'s
prerequisite reads scored as correct). Two clean runs:

| Cluster | after pass 2 (best) | run A | run B |
| --- | ---: | ---: | ---: |
| capability | 100% | 100% | 100% |
| exact-fact | 100% | 100% | 100% |
| bulk-sweep | 100% | 100% | 100% |
| catalog | 100% | 100% | 100% |
| shortlist | 100% | 75% | 100% |
| style | 0% | **100%** | **100%** |
| writes | 83% | **100%** | **100%** |
| **overall first-tool** | **0.872** | **0.974** | **1.000** |

0 transport errors in both. The single miss in run A is the familiar `sl-jd-paste`, landing on
`skill_list` this time — the one prompt that has never settled across any run of this eval.

**What this says.** The two description passes moved the aggregate about 2 points, inside the
run-to-run noise. The two affordance fixes moved it roughly 12, and moved the two clusters that
descriptions had never once shifted — `style` off a flat 0% across six runs, `writes` off 75–83%.
The instrument's real finding was never "the wording is wrong"; it was "these tool contracts force
a read the prompt cannot satisfy". Fixing the contracts is what paid.

**Floors NOT tightened yet — deliberately.** The floor policy above wants three runs and a floor at
the minimum minus headroom. The day's free-tier quota (500 requests, 39 per run) ran out after two
clean runs, and this eval has been misled by two agreeing runs twice already. Two is exactly the
trap the policy exists for, so the committed floors stay where pass 2 left them until a third clean
run lands. Tighten then, from the minimum of three.

**A run past the error ceiling is not a measurement.** Two of the day's five runs died partway
through on quota and rendered as `writes 0%` / near-total collapse — indistinguishable, in the
report as it stood, from a real regression. The runner reported every HTTP fault as the OpenAI-compat
client's `Service request failed.`, with no status. Fixed under P1T-137: a fault now carries its HTTP
status and the service's own message, so a 429 reads as a 429, and any run over the error ceiling is
flagged **"Not a usable measurement"** at the top of the report. Check that line before believing a
cluster fell.

One practical note for whoever takes the third run: the runner paces prompts 4s apart, which is
15 requests/minute — right at this model's free-tier per-minute limit, so a run can trip the rate
window as well as the daily cap. Run the eval first, before anything else that spends quota.

### The floor policy, learned the hard way twice

Both passes set a floor from two agreeing runs and had a third run trip it — pass 1 looked like a
clean 0.846 until run 3 read 0.821; pass 2 looked like 0.846/0.872 until run 3 read 0.821 with
`write-draft` making **no tool call at all** and `sl-jd-paste` landing on `skill_list`. So:

**Take at least three runs. Floor at the minimum observed, minus headroom. Never tighten a floor to
express a hope.**

Committed floors: global **0.79** (min observed 0.821 across ten runs, minus one prompt), and
per-cluster first-tool floors — capability / exact-fact / bulk-sweep / catalog at **100%**,
shortlist and writes at **0.75**, style ungated.

The uncomfortable consequence, stated plainly: run-to-run variance here is worth about two prompts,
which is **larger than either pass's aggregate gain**, so the global floor cannot detect losing that
gain. The four read clusters — 100% in all ten runs — are the only gate worth its name, and the
write/shortlist floors catch a collapse, not a slip. P1T-138 is the fix for the instrument itself
(pin temperature, re-baseline; optionally score a majority over N samples per prompt), and until it
lands the typical figures in the table above are the honest description of the pass, while the
floors are the honest description of what can be *enforced*.

### What is left is not wording: required arguments the prompt cannot supply

Five misses survive both passes, and they share one shape — the expected tool has a **required
argument the prompt never provides**, so the model calls something it can legally call. In three of
the five that deflection is arguably the *correct* move:

| Prompt | Expected | Model calls | The blocking contract |
| --- | --- | --- | --- |
| `write-update-title` | `employee_update` | `employee_get` | `SaveEmployeeDto` is a full replace with `firstName`/`lastName` `NotEmpty` — writing from the prompt alone would blank the person's name and email |
| `write-skill-trap-catalog` | `skill_create` | `category_list` | needs a `categoryId` the prompt never gives (its sibling prompt, which supplies one, passes) |
| `style-bullet` / `style-rewrite` / `style-metrics` | `style_exemplar_search` | `roster_semantic_search` etc. | needs `achievementIds`; the prompts name no bullet |
| `write-draft` (last two runs only) | `employee_create_draft` | **no call at all** | the prompt says "stage this pasted resume" but carries no resume text, so there is nothing to put in the dto |

`write-draft` deserves a note on provenance rather than a theory: it passed in the first eight runs
and made no call in the last two. Those last two are also the first runs that saw a one-sentence
edit to `employee_create`'s description (the email-uniqueness wording). A causal link is unlikely —
the prompt is about drafts, not creates — and two runs of a ±2-prompt instrument cannot establish
one either way. Left recorded, not concluded; P1T-138's variance fix is what would settle it.

The eval scores a first call, and `AnyCallCorrect` cannot rescue these either — the harness
measures one turn, so the follow-up write never happens. Tracked as P1T-137 (write side: partial
update for `employee_update`, `skill_create` by category name, or a deliberate re-label) and
P1T-136 (style side). Both decisions are a human's: one option changes the product surface, the
other changes the yardstick.

#### The write cluster's two survivors, resolved (P1T-137)

Landed **option 1** for `employee_update`, **option 3** for `skill_create`'s trap prompt:

* `employee_update` now takes `UpdateEmployeeDto` — every root field optional, only the fields
  present overwrite the employee's current value (`FirstName`/`LastName`, if sent, still cannot be
  blank). The old `SaveEmployeeDto` full-replace path is unchanged for `employee_create` and stays
  available on REST as `PUT /api/employees/{id}`; a new `PATCH /api/employees/{id}` exposes the
  same partial-update method REST-side, so validation stays identical across both surfaces
  (product invariant 7). `write-update-title` — "Change the title of employee … to Staff
  Engineer" — is now a single legal `employee_update` call with just `{"title": "Staff Engineer"}`;
  no re-label needed, this was a real product gap in the tool contract, not an eval sensitivity to
  loosen. The tool description dropped the "full replace" warning for one describing partial-update
  semantics and the null-vs-empty-string distinction.
* `skill_create` keeps its required `categoryId` — inventing a default/"Uncategorized" category
  (option 2) would let an agent silently miscategorize a new catalog entry, a worse failure than a
  prerequisite read. `write-skill-trap-catalog` — "Create a brand-new skill entry called 'Zig'…" —
  is re-labeled with `AlsoAcceptable: ["category_list", "category_tree", "skill_list"]`: the prompt
  never supplies a category, so a prerequisite read is the legally correct first call, matching
  `write-new-catalog-skill`'s sibling prompt that does supply one.

Application-layer tests cover `PatchAsync`: a title-only patch leaves the other fields untouched, a
supplied empty `firstName` still throws `ValidationException`, and an unknown id still throws
`NotFoundException` (`tests/Application.Tests/EmployeeServiceValidationTests.cs`). An MCP-level
test confirms the same round trip through `employee_update` with a single-field dto
(`tests/Mcp.Tests/EmployeeToolsTests.cs`).

**Eval re-run: done, 2026-08-29 — see "After the affordances" above.** The `writes` cluster read
**100% in both clean runs**, up from 83% at its pass-2 best. Both survivors are gone:
`write-update-title` now calls `employee_update` directly, and `write-skill-trap-catalog`'s
prerequisite `category_list` read is scored as correct.

The honest summary of two passes: the description bar fixed what descriptions can fix — the
read clusters held at 100%, the JD prompt left the plain roster listing, `experience_add` won its
prompt back from `skill_list` — and then the instrument stopped rewarding wording and started
pointing at tool contracts instead. That is the more useful finding of the two.

#### The style cluster in detail

Three prompts ask for phrasing help without naming a bullet ("show me examples of strongly phrased
achievement bullets about cost reduction"). `style_exemplar_search` has a **required**
`achievementIds` argument, so the model — told to start with a tool call and to use only the ids
the user gave — deflects to something it can actually fill: `roster_semantic_search`, `cv_get`, or
`roster_digest_list`. Two description edits were tried and measured (a phrasing-first "when NOT"
clause on `roster_semantic_search`, an explicit "this is the phrasing tool" line on
`style_exemplar_search`); neither moved the cluster off 0% in any of the six runs since. The fix is
one of:

- make `achievementIds` optional with a free-text query fallback (a product change to
  `IExemplarSearchService` — exemplars for a described theme, not only for a specific bullet);
- or accept the prompts as measuring an unreachable state and re-label them (frozen set — a
  deliberate re-label only, with the before/after re-baselined).

Recorded here rather than guessed at: the honest read is that the golden set caught a real gap in
the tool surface, which is exactly what it is for. Tracked as P1T-136 — the choice between the two
options is a human's, since one changes the product surface and the other changes the yardstick.

**Decision (P1T-136): option 1 — the affordance is now real.** `IExemplarSearchService.SearchAsync`
takes `achievementIds` OR a free-text `theme`, mutually exclusive (both or neither is a `validation`
error with per-field detail, not a silent empty result). Theme mode embeds the theme itself and
ranks the whole achievement-bullet pool against it; unlike id mode there is no requesting employee,
so nothing is excluded from the pool. The response shape says honestly which mode ran:
`ExemplarSearchResult.Results` (id-keyed, a `BulletExemplars` per input bullet) and `ThemeResult`
(a single `ThemeExemplars`) are siblings, not a nullable-keyed variant of one record — exactly one
is populated. `style_exemplar_search`'s description now states both modes and the P1T-128 template
still applies. Unit tests cover the id-less path with the fake embedder (including the no-owner-
exclusion behavior, contrasted directly against the id-keyed empty-result case for the same
underlying data) and confirm anonymization still runs.

**Eval re-run: done, 2026-08-29 — see "After the affordances" above.** The `style` cluster went
**0% → 100% in both clean runs**: all three theme-only prompts now call `style_exemplar_search`
first. Its floor is still ungated pending a third clean run, for the reason given there.

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
