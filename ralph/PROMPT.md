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
   `npm test`, `npm run typecheck` and `npm run lint` in `web/` if you touched the SPA. Green
   before you commit. If it will not go green, say so in the ticket and stop — do not commit red.
6. **Land it.** Commit, push the branch, open a PR with `gh pr create`.
   **Do not merge.** Merging is gated deliberately (`Bash(gh pr merge:*)` is an ask-rule); a
   human takes it from the PR.
7. **Record it.** Comment on the Linear issue with what you did and the PR link, then leave the
   issue `In Progress` — a human moves it to `Done` when the PR merges.

## Rules

- **One ticket per iteration.** Not two, not "while I'm in here". The loop gives you another turn.
- Respect the repo's standing conventions: tracked docs go in `/manuals` (`/docs` is gitignored),
  no stacked PRs — branch from `main`, and never from another unmerged branch.
- If the ticket turns out to be wrong, blocked in reality, or already done, say so in a Linear
  comment, move it back to `Todo`, and stop. A wrong ticket is a finding, not a thing to force.

## Nothing to do

If no `ready-for-agent` ticket is takeable — the queue is empty, or everything left is blocked by
something unfinished — output exactly:

<promise>COMPLETE</promise>

Output that sigil **only** when it is genuinely true. Do not emit it to escape a hard ticket.
