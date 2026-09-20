#!/bin/bash
# Ralph, unattended, in a Docker sandbox.
#
#   ./ralph/afk-ralph.sh <iterations>
#
# Each iteration is a fresh `claude -p` run inside the `claude-AITestApp` sandbox, working
# one Linear ticket. The loop stops early when the agent reports the queue is drained by
# emitting <promise>COMPLETE</promise>.
#
# WHICH TREE THE LOOP OWNS (P1T-224)
#
# The sandbox runs in `--clone` mode: the agent works on a private clone *inside* the container,
# and this checkout is mounted read-only at /run/sandbox/source. That is not a nicety. Under the
# old bind mount the agent's `git checkout` moved HEAD in this working tree, and a developer
# committing here at the wrong moment landed their work on whatever branch the agent had left
# behind — which is how P1T-216 went straight to `main`.
#
# The agent's commits come back as refs under refs/sandboxes/<sandbox>/<branch>; it also has a
# real GitHub `origin`, so its own `git push` and `gh pr create` work unchanged.
#
# Prerequisites:
#   - sbx installed, Docker running
#   - the Linear MCP server authorized INSIDE the sandbox:  sbx mcp auth linear-server
#     (an expired credential is the usual cause of a loop that burns iterations doing nothing)
#     The backlog moved to the `experttojob` workspace on 2026-09-20, under a different Linear
#     account. A sandbox credential minted before that still authorizes the OLD workspace, where
#     the loop finds an empty queue and reports COMPLETE having done nothing — which looks like a
#     drained backlog, not a broken credential. Re-auth after the move, and check that the agent's
#     first tool call sees team `ExpertToJob`.
#   - a stored GitHub secret the sandbox can use:  sbx secret ls
#     A freshly created sandbox with no github secret gets "Bad credentials" from every gh call,
#     so it can do the work and then fail to open the PR. Scope it globally rather than to one
#     sandbox name, or the next recreate loses it again.

set -euo pipefail

cd "$(dirname "$0")/.."

if [ -z "${1:-}" ]; then
  echo "Usage: $0 <iterations>" >&2
  exit 1
fi

SANDBOX="claude-AITestApp"
# The sandbox image, and the only way the .NET SDK gets in: the egress allowlist permits
# api.nuget.org but returns 403 for every SDK download host, so an agent cannot install it for
# itself. Recreating the sandbox without this silently costs the loop `dotnet build` and
# `dotnet test` (P1T-226). Rebuild it with: sbx template save <a sandbox with the SDK> "$TEMPLATE"
TEMPLATE="claude-dotnet10:v1"
PROMPT="$(cat ralph/PROMPT.md)"
LOGDIR="ralph/logs"
mkdir -p "$LOGDIR"

for ((i = 1; i <= $1; i++)); do
  LOG="$LOGDIR/$(date -u +%Y%m%dT%H%M%SZ)-iter$i.jsonl"
  echo "=== ralph iteration $i/$1 -> $LOG ==="

  # --static-mcp and --clone are both fixed when the sandbox is created and are rejected on
  # re-attach, so only the iteration that actually creates the sandbox may pass them. Re-attaching
  # keeps the same MCP set (and its authorization), which is why the sandbox is reused rather than
  # recreated per run.
  create_flags=()
  if ! sbx list 2>/dev/null | awk 'NR > 1 { print $1 }' | grep -qx "$SANDBOX"; then
    create_flags=(--clone --static-mcp linear-server -t "$TEMPLATE")
  elif ! sbx exec "$SANDBOX" -- sh -c 'command -v dotnet' >/dev/null 2>&1; then
    # The template is what puts the .NET SDK in the sandbox, and it cannot be added afterwards:
    # the egress allowlist permits api.nuget.org but 403s every SDK download host. A sandbox
    # created without it looks fine and then cannot build or test anything — which is exactly how
    # P1T-226 happened. Refuse, rather than let an agent quietly fall back to "CI will tell me".
    echo "!! sandbox '$SANDBOX' has no dotnet: it was created without -t $TEMPLATE" >&2
    echo "   Recreate it so the loop can build and test:" >&2
    echo "     sbx rm --force $SANDBOX" >&2
    exit 1
  elif ! sbx exec "$SANDBOX" -- test -d /run/sandbox/source >/dev/null 2>&1; then
    # An existing bind-mount sandbox would silently reintroduce P1T-224: its container can write
    # this working tree and move HEAD under whoever else is using it. Refuse rather than re-attach.
    echo "!! sandbox '$SANDBOX' predates --clone: it bind-mounts this checkout and can move HEAD" >&2
    echo "   under a concurrent session. Recreate it, then re-check the GitHub secret:" >&2
    echo "     sbx rm --force $SANDBOX && sbx secret ls" >&2
    exit 1
  fi

  # sbx's own -p means --publish, so every agent flag goes after the -- separator.
  # tee keeps the raw stream for the promise check; format.sh renders it live.
  set +e
  # ${a[@]+"${a[@]}"} rather than plain "${a[@]}": macOS ships bash 3.2, where `set -u` treats an
  # empty array expansion as an unbound variable, and the array is empty on every re-attach.
  sbx run claude --name "$SANDBOX" ${create_flags[@]+"${create_flags[@]}"} -- \
    --permission-mode acceptEdits \
    --output-format stream-json \
    --verbose \
    -p "$PROMPT" 2>&1 | tee "$LOG" | ./ralph/format.sh
  status=${PIPESTATUS[0]}
  set -e

  if [ "$status" -ne 0 ]; then
    echo "!! sbx exited $status on iteration $i - see $LOG" >&2
    exit "$status"
  fi

  if grep -q '<promise>COMPLETE</promise>' "$LOG"; then
    echo "== queue drained after $i iteration(s)."
    exit 0
  fi
done

echo "== stopped at the $1 iteration cap; queue not drained."
