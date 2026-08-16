# MCP tool reliability: sequencing audit (and, next, the description pass)

The tool-reliability bundle's written record (P1T-112). This file starts with the **sequencing
audit** (P1T-131); the description-pass sections (template, per-cluster rewrites, before/after
eval numbers) land here with P1T-128/129.

## The rule

**Fixed order → code. Dynamic order → prompt + error-driven retry.**

If a tool call is a *fixed prerequisite* — it always happens, with arguments fully known before
the model says a word — hoping the tool loop performs it buys nothing and risks a skipped call,
a malformed argument, or a wasted round-trip. Invoke it in code (the P1T-117 shortlist-retrieval
pattern) and hand the captured result to the model. If the call is *genuinely dynamic* — the
model decides whether/what/with-which-arguments from mid-run reasoning — it stays model-driven,
steered by the prompt, with structured tool errors driving self-correction and (where grounding
is mandatory) `RequireAny` forcing plus the Capture-Verify Guard (P1T-130).

## The audit

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

## What the Tailoring conversion changed (P1T-131)

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
