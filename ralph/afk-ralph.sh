#!/bin/bash
# Ralph, unattended, in a Docker sandbox.
#
#   ./ralph/afk-ralph.sh <iterations>
#
# Each iteration is a fresh `claude -p` run inside the `claude-AITestApp` sandbox, working
# one Linear ticket. The loop stops early when the agent reports the queue is drained by
# emitting <promise>COMPLETE</promise>.
#
# Prerequisites:
#   - sbx installed, Docker running
#   - the Linear MCP server authorized INSIDE the sandbox:  sbx mcp auth linear-server
#     (an expired credential is the usual cause of a loop that burns iterations doing nothing)

set -euo pipefail

cd "$(dirname "$0")/.."

if [ -z "${1:-}" ]; then
  echo "Usage: $0 <iterations>" >&2
  exit 1
fi

SANDBOX="claude-AITestApp"
PROMPT="$(cat ralph/PROMPT.md)"
LOGDIR="ralph/logs"
mkdir -p "$LOGDIR"

for ((i = 1; i <= $1; i++)); do
  LOG="$LOGDIR/$(date -u +%Y%m%dT%H%M%SZ)-iter$i.jsonl"
  echo "=== ralph iteration $i/$1 -> $LOG ==="

  # sbx's own -p means --publish, so every agent flag goes after the -- separator.
  # tee keeps the raw stream for the promise check; format.sh renders it live.
  set +e
  sbx run claude --name "$SANDBOX" --static-mcp linear-server -- \
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
