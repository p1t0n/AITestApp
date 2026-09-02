// The freeze from `manuals/spa-design-system.md` §9, as code — P1T-158's closing pass over its own
// six children.
//
// §9's first row says why the hooks matter: they are the unit suite's grip on the DOM, and renaming
// one is a silent test deletion. The chain then spent five DOM-changing slices claiming that grip
// survived, and the claim held — but it held against a table in a manual, which is the one thing
// this record says a rule must not be ("a rule here that no code enforces is a lie, not a plan").
// Verified once by hand at the end of the chain against `f10d27b`, the commit before slice 1
// merged: nothing was removed, one hook was added. This file is that check, kept.
//
// Two things it found that the table did not say:
//
//   - The freeze was written as "25 `data-testid` hooks" and counted only the string literals. The
//     grip is 39: 26 literals plus 13 **templated** hooks (`proposal-row-${p.id}` and friends),
//     which are just as renameable and were never named anywhere. They survived the chain by luck
//     rather than by a net.
//   - Four of the 39 are referenced by no test in `src/` or `e2e/` at all — `error-notice`,
//     `matched-icon`, `staffing-stepper` and `staffing-evidence-*`. For those four, "renaming one
//     is a silent test deletion" was not even true: there was nothing to delete. This file is what
//     makes it true, which is the cheapest available answer — a rendering assertion per hook buys
//     nothing over the name check when no suite wanted the hook in the first place.
//
// What this holds is the *name*, not the behaviour: that a hook is still on the right element doing
// the right job is held by the suite that queries it (the ten `AgentWidget.*` files are the
// tightest net in this repo). A name check is strictly the part no suite was holding.
import fs from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

/** The 25 literal hooks the epic froze, as they stood at `f10d27b`. */
const FROZEN_LITERALS = [
  "bench-notes",
  "bench-proposals",
  "bench-stats",
  "draft-status-chip",
  "error-notice",
  "ingestion-dupe-warning",
  "ingestion-notes",
  "ingestion-review",
  "matched-icon",
  "missed-icon",
  "proposal-decided",
  "proposal-decision",
  "proposal-degradations",
  "proposal-drill-in",
  "proposal-inbox",
  "proposal-no-package",
  "proposal-provenance",
  "scan-estimate",
  "scan-paused",
  "snippet",
  "staffing-band-chip",
  "staffing-degraded",
  "staffing-match-tick",
  "staffing-recommendation",
  "staffing-stepper",
] as const;

/**
 * The one hook the chain itself added, and frozen from here.
 *
 * Slice 3 (P1T-161) replaced the `AppBar` with the rail and gave the identity block its own hook.
 * It is listed separately rather than folded into the 25 because the distinction is the whole
 * finding: the epic's freeze is what came *in* to the chain, and this is what the chain left behind.
 */
const ADDED_BY_THE_CHAIN = ["rail-user"] as const;

/**
 * The templated hooks, with each `${…}` normalised to `*`.
 *
 * All 13 predate the chain and came through it byte-for-byte. They are held here for the same
 * reason as the literals and were missing from §9 for no reason anyone recorded.
 */
const FROZEN_TEMPLATES = [
  "evidence-*",
  "evidence-row-*",
  "inferred-*",
  "interview-question-*",
  "jd-match-*",
  "proposal-*",
  "proposal-row-*",
  "rewrite-card-*",
  "rewrite-group-*",
  "scan-row-*",
  "staffing-candidate-*",
  "staffing-evidence-*",
  "staffing-step-*",
] as const;

/**
 * Hooks added since, each with the slice that added it and why — the file's own convention, because
 * the distinction between "what came in" and "what we left behind" is the finding it records.
 *
 * `row-*` addresses one right on the privacy page by its label (P1T-191). Two of those rows carry a
 * control-word field — objecting and deleting are the same act reached two ways — so a page-wide
 * query cannot tell them apart, and the hook is what lets a test scope to one right.
 */
const ADDED_SINCE_THE_CHAIN = ["row-*"] as const;

// Off the working directory rather than off `import.meta.url`: the jsdom environment rewrites
// `import.meta.url` to an `http://localhost/…` URL, so `fileURLToPath` throws on it. Vitest runs
// with the cwd at `web/` (`e2e/screenshots.e2e.ts` already relies on the same), and the read below
// throws by itself if that ever stops being true, which is the failure a reader wants.
const SRC = path.resolve("src");

/**
 * Every file the app ships, test files excluded on purpose: a hook that exists only inside a
 * `*.test.tsx` is a hook the app does not emit, and counting it would let a rename pass by moving
 * the string into the suite that asserts it.
 */
function appSources(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) return appSources(full);
    if (!/\.tsx?$/.test(entry.name)) return [];
    if (/\.test\.tsx?$/.test(entry.name)) return [];
    return [full];
  });
}

function emittedHooks(): { literals: Set<string>; templates: Set<string> } {
  const literals = new Set<string>();
  const templates = new Set<string>();
  for (const file of appSources(SRC)) {
    const source = fs.readFileSync(file, "utf8");
    for (const [, id] of source.matchAll(/data-testid="([^"]+)"/g)) literals.add(id);
    for (const [, id] of source.matchAll(/data-testid=\{`([^`]+)`\}/g)) {
      templates.add(id.replace(/\$\{[^}]*\}/g, "*"));
    }
  }
  return { literals, templates };
}

const missing = (frozen: readonly string[], emitted: Set<string>) =>
  frozen.filter((hook) => !emitted.has(hook));

describe("frozen DOM hooks (P1T-158 §9)", () => {
  it("still emits every literal hook frozen before the chain", () => {
    expect(missing(FROZEN_LITERALS, emittedHooks().literals)).toEqual([]);
  });

  it("still emits every templated hook frozen before the chain", () => {
    expect(missing(FROZEN_TEMPLATES, emittedHooks().templates)).toEqual([]);
  });

  it("still emits the hook the chain added", () => {
    expect(missing(ADDED_BY_THE_CHAIN, emittedHooks().literals)).toEqual([]);
  });

  it("still emits the hooks added since", () => {
    expect(missing(ADDED_SINCE_THE_CHAIN, emittedHooks().templates)).toEqual([]);
  });

  // The other direction, and the reason this is an inventory rather than three presence checks: a
  // rename shows up above as a removal, but a *new* hook shows up nowhere unless the set is closed.
  // Failing here is not a defect — it means a hook arrived and this list is the place to say so,
  // which is exactly the deliberate edit §9 is asking for.
  it("names every hook the app emits", () => {
    const { literals, templates } = emittedHooks();
    expect([...literals].sort()).toEqual([...FROZEN_LITERALS, ...ADDED_BY_THE_CHAIN].sort());
    expect([...templates].sort()).toEqual(
      [...FROZEN_TEMPLATES, ...ADDED_SINCE_THE_CHAIN].sort());
  });
});
