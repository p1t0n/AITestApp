You are one iteration of a Ralph loop on the ExpertToJob repo (formerly CvManager).

The backlog is **Linear**, not a file. Team `P1t0ns nest`, project `AI Test Manager`.
Reach it through the `linear-server` MCP tools. There is no PRD.md and no progress.txt —
Linear issue state *is* the progress file.

## Do exactly one ticket, then stop

1. **Pick the ticket.** List issues in project `AI Test Manager` with label `ready-for-agent`
   and state `Todo`. Discard any whose `blockedBy` issues are not yet `Done` — blocking is
   native and load-bearing here, never ignore it. Of what remains take the highest priority
   (1=Urgent first), breaking ties by lowest issue number. If nothing is takeable, skip to
   "Nothing to do" below.
2. **Claim it.** Set the issue to `In Progress` and assign it to Roman Yurkin before any work,
   so a concurrent iteration cannot pick it up too.
3. **Branch.** Use the issue's own `gitBranchName` from Linear verbatim — it carries the issue
   key, which is what makes Linear auto-link the PR.
4. **Build it TDD.** Red, green, refactor. The ticket's acceptance criteria are the spec; do not
   invent scope beyond them and do not silently drop a criterion you found inconvenient.
5. **Prove it.** Run the full suite: `dotnet test --filter "Category!=e2e&Category!=live"`, plus
   `npm test`, `npm run typecheck`, `npm run lint` **and `npm run test:e2e`** in `web/` if you
   touched the SPA — the e2e suite owns its own stack and needs Docker, and it is the only thing
   that sees a real cascade, a real print media and a real browser. A theme change that passes
   jsdom and breaks Playwright is a normal Tuesday. Green before you commit. If it will not go
   green, say so in the ticket and stop — do not commit red.
6. **Land it.** Commit, push the branch, open a PR with `gh pr create`.
   **Do not merge.** Merging is gated deliberately (`Bash(gh pr merge:*)` is an ask-rule); a
   human takes it from the PR.
7. **Watch CI to a conclusion.** A pushed PR is not a landed one. Wait for the run and read it:

   ```bash
   gh pr checks <pr>                                  # the three jobs and their state
   gh run watch <run-id> --exit-status --interval 20  # blocks until the run finishes
   ```

   For a failure, get the actual assertion rather than guessing — `gh run view <id> --log-failed`
   is truncated for long jobs, so when it does not carry the failure, pull the whole archive:

   ```bash
   gh api repos/p1t0n/AITestApp/actions/runs/<id>/logs > logs.zip && unzip -q logs.zip -d logs
   grep -nE "  Failed [A-Z]|Test Run Failed" "logs/0_Build & Test.txt"
   ```

   Then decide **whose** red it is, and say which in the ticket:

   * **Yours** — fix it on the branch and push again. Iterate until green; a red PR is not done.
   * **Already red on `main`** — check `gh run list --branch main --limit 3` before assuming.
     Inherited red is a finding: it gets its own Linear issue with the diagnosis, and it does not
     get "fixed" by loosening the assertion that caught it. Say plainly that the branch's own
     jobs are green and which job is inherited.
   * **Green locally, red on CI (or the reverse)** — the environments differ in ways that matter:
     culture and time zone, `Release` vs `Debug`, a clean database per run. A test that passes
     here and fails there is evidence about the *test*, not noise to re-run away.

   CI is the last word on green, not the local run. Report a ticket done only when CI says so.
8. **Record it.** Comment on the Linear issue with what you did, the PR link and the CI result,
   then leave the issue `In Progress` — a human moves it to `Done` when the PR merges.

   **Report only commands you actually ran.** Every number you quote — a test count, a row count,
   an exit code — has to come from output you saw in this session. Do not describe what a command
   would have printed. A check you could not run is reported as *why*, not as a result:
   "no local `dotnet test` — this sandbox has no .NET SDK; CI covers it at <link>" is a complete
   and welcome answer, and it costs you nothing.

   This is not pedantry about phrasing. A test report is worse than useless when it cannot be
   told apart from a real one: confident, specific, plausible, attached to a green PR, and
   impossible to audit later. Naming the tool and the run is what makes it checkable.

   It cuts the other way too, which is the lesson of P1T-226. A reviewer there found no .NET SDK
   in the sandbox, concluded a comment reporting "992 passed locally" must have been invented, and
   filed a ticket saying so — when in fact the sandbox had been rebuilt without its template and
   had silently lost the SDK it used to have. The report was true; the environment had changed
   under it. So: say which environment you ran in, not only which command.

   **What this sandbox can and cannot do**, so you do not have to guess. Each line was measured,
   not assumed:

   * `npm` — yes. `npm test`, `npm run typecheck`, `npm run lint`.
   * `docker` — yes, including Testcontainers: the Postgres and Keycloak suites really do start
     containers here.
   * `dotnet` — **yes**, 10.0.400. `dotnet restore`, `build`, `test` and `run` all work; the full
     backend suite passes in about two minutes. The SDK is not part of the base image — it was
     copied in from the host (P1T-226) because the egress allowlist permits `api.nuget.org` but
     returns 403 for every SDK download host. If `dotnet: command not found` ever greets you, the
     sandbox was created without the saved template; say so in the ticket rather than working
     around it, and fall back to CI.
   * `npm run test:e2e` — **no**. The stack starts, but Playwright's browser download is blocked
     by the same allowlist, so every spec fails on `browserType.launch: Executable doesn't exist`.
     Leave e2e to CI, which runs it on every PR.
   * A browser, a dashboard, anything you look at — no. If a ticket's acceptance needs eyes, say
     so and leave that criterion to a human rather than asserting it.

## Rules

- **One ticket per iteration.** Not two, not "while I'm in here". The loop gives you another turn.
- **The tree you are in is yours alone.** You work on a private clone inside the sandbox; the
  developer's checkout is mounted read-only at `/run/sandbox/source` and you cannot write it. So
  branch and commit freely — but never assume the human's checkout looks like yours, and never
  reach into `/run/sandbox/source` to "fix" anything. Your commits reach the host as refs under
  `refs/sandboxes/<sandbox>/<branch>`, and your `origin` is the real GitHub remote, so pushing a
  branch and opening a PR works exactly as it reads (P1T-224).
- Respect the repo's standing conventions: tracked docs go in `/manuals` (`/docs` is gitignored),
  no stacked PRs — branch from `main`, and never from another unmerged branch.
- If the ticket turns out to be wrong, blocked in reality, or already done, say so in a Linear
  comment, move it back to `Todo`, and stop. A wrong ticket is a finding, not a thing to force.

## Nothing to do

If no `ready-for-agent` ticket is takeable — the queue is empty, or everything left is blocked by
something unfinished — output exactly:

<promise>COMPLETE</promise>

Output that sigil **only** when it is genuinely true. Do not emit it to escape a hard ticket.
