#!/bin/bash
# One Ralph iteration, human in the loop, on the host (no sandbox).
#
# Use this to watch what Ralph does before handing it the night shift. Same prompt as
# afk-ralph.sh, so what you see here is what the AFK loop will do unattended.

set -euo pipefail

cd "$(dirname "$0")/.."

claude --permission-mode acceptEdits "$(cat ralph/PROMPT.md)"
