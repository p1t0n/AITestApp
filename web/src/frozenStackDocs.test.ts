// The last Aspire slice (P1T-219) deleted the compose file, so "bring the local stack up" has
// exactly one documented spelling: `dotnet run --project api/AppHost`.
//
// `manuals/adr-aspire-apphost.md` is explicit that the old file is deleted *rather than kept as a
// fallback*, and gives the reason: "two ways to start a stack means two configs drifting". The
// drift does not need the file back to start — a README, a skill or a manual that still tells the
// reader to run the retired command is already the second config, and it is the only door the
// deletion left open. So the rule is held here rather than in prose.
//
// Two files are exempt, both deliberately:
//
//   - `SPEC.md` is the original POC write-up. `CLAUDE.md` documents it as drifted (it still names
//     GitHub Models as the chat backend), and P1T-219 was told to check it and *not* fix it.
//   - `manuals/adr-aspire-apphost.md` is the record of the deletion itself. A decision record that
//     may not name the thing it retired cannot state its own decision.
//
// This is a name check, not a behaviour check: that the AppHost actually starts the stack is held
// by running it (P1T-225), not by a string in a doc.
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";

// Off the working directory, like `frozenHooks.test.ts`: vitest runs from `web/`, and the jsdom
// environment rewrites `import.meta.url` into an `http://localhost/…` URL that `fileURLToPath`
// throws on.
const REPO = path.resolve("..");

/** Files allowed to name the retired orchestrator, and why. */
const EXEMPT = new Set(["SPEC.md", "manuals/adr-aspire-apphost.md"]);

/** Tracked files matching `pattern`, repo-relative. `git grep -I` skips binaries for us. */
function trackedFilesMatching(pattern: string): string[] {
  const result = spawnSync("git", ["grep", "-lIiE", pattern], { cwd: REPO, encoding: "utf8" });
  // 1 is "no match", which is the answer we are hoping for rather than a failure.
  if (result.status === 1) return [];
  if (result.status !== 0) throw new Error(result.stderr || `git grep exited ${result.status}`);
  return result.stdout.split("\n").filter(Boolean);
}

describe("the stack has one documented way to start", () => {
  it("has no compose file at the repo root", () => {
    // Spelled in pieces for the same reason as the pattern below: so this file is not itself an
    // offender the freeze has to exempt.
    const retired = ["docker", "compose"].join("-");
    const candidates = [`${retired}.yml`, `${retired}.yaml`, "compose.yml", "compose.yaml"];
    const present = candidates.filter((name) => fs.existsSync(path.join(REPO, name)));
    expect(present).toEqual([]);
  });

  it("has no tracked file telling the reader to use it", () => {
    // The bracket keeps this file's own source off the match list: neither spelling appears here
    // literally, so the freeze does not have to exempt itself.
    const offenders = trackedFilesMatching("docker[- ]compose").filter((f) => !EXEMPT.has(f));
    expect(offenders).toEqual([]);
  });

  it("still documents every pinned port", () => {
    // The ADR's whole "no service discovery" argument rests on these staying literal, so a doc
    // rewrite that quietly dropped one would be the expensive kind of tidy.
    const readme = fs.readFileSync(path.join(REPO, "README.md"), "utf8");
    for (const port of ["5069", "5100", "5200", "5173", "5432", "8080"]) {
      expect(readme, `README.md no longer names port ${port}`).toContain(port);
    }
  });

  it("names the AppHost as the one command, in every doc that starts the stack", () => {
    const command = "dotnet run --project api/AppHost";
    for (const doc of ["README.md", "CLAUDE.md", ".claude/skills/verify/SKILL.md"]) {
      const source = fs.readFileSync(path.join(REPO, doc), "utf8");
      expect(source, `${doc} does not name the AppHost command`).toContain(command);
    }
  });
});
