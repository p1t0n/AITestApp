#!/bin/bash
# Self-check for format.sh: ./ralph/format.test.sh  (exit 0 = pass)
set -euo pipefail
cd "$(dirname "$0")"

input=$(printf '%s\r\n' \
  $'\e]0;title\a\e[2J\e[H{"type":"system","subtype":"init","session_id":"abcdef1234","model":"m","tools":[1,2]}' \
  '{"type":"assistant","message":{"content":[{"type":"text","text":"path C:\\new\\t"},{"type":"tool_use","name":"Bash","input":{"command":"ls","description":"List files"}},{"type":"tool_use","name":"mcp__mcp-gateway__get_issue","input":{"id":"EXP-71"}}]}}' \
  '{"type":"user","message":{"content":[{"type":"tool_result","is_error":false,"content":"fine"},{"type":"tool_result","is_error":true,"content":"Exit code 1\ncd: web: No such file"}]}}' \
  $'plain sbx line\e[?25h\e\\')

got=$(printf '%s' "$input" | ./format.sh | sed $'s/\e\\[[0-9;]*m//g')
want=$(cat <<'EOF'
> session abcdef12 | m | 2 tools

path C:\new\t
  * Bash List files
  * get_issue EXP-71
    <- cd: web: No such file
plain sbx line
EOF
)

if [ "$got" != "$want" ]; then
  diff <(printf '%s\n' "$want") <(printf '%s\n' "$got") >&2 || true
  exit 1
fi
echo "format.sh ok"
