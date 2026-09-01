#!/bin/bash
# Translate `claude --output-format stream-json` into readable terminal lines.
#
# sbx hands us the agent's raw event stream on stdout; unformatted that is a wall of
# single-line JSON. This renders it. Non-JSON lines (sbx's own chatter, docker noise)
# pass through untouched, so nothing is silently swallowed.
#
# Usage:  sbx run ... | ./ralph/format.sh

DIM='[2m'
CYAN='[36m'
RED='[31m'
BOLD='[1m'
OFF='[0m'

jq -Rr --unbuffered \
  --arg dim "$DIM" --arg cyan "$CYAN" --arg red "$RED" --arg bold "$BOLD" --arg off "$OFF" '
  . as $line
  | (try fromjson catch null) as $e
  | if $e == null then
      $line
    elif $e.type == "system" and $e.subtype == "init" then
      "\($dim)> session \($e.session_id[0:8]) | \($e.model // "?") | \($e.tools | length) tools\($off)"
    elif $e.type == "assistant" then
      ( $e.message.content[]?
        | if .type == "text" then
            (.text | select(length > 0))
          elif .type == "tool_use" then
            "\($cyan)  * \(.name)\($off)"
          else empty end )
    elif $e.type == "user" then
      ( $e.message.content[]?
        | select(.type == "tool_result")
        | if (.is_error // false)
          then "\($red)  <- error\($off)"
          else "\($dim)  <- ok\($off)" end )
    elif $e.type == "result" then
      "\($bold)# \($e.subtype) | \($e.num_turns // 0) turns | \((($e.duration_ms // 0) / 1000) | floor)s"
      + (if $e.total_cost_usd
         then " | $\(($e.total_cost_usd * 100 | round) / 100)"
         else "" end)
      + "\($off)"
    else empty end
' | while IFS= read -r l; do printf '%b\n' "$l"; done
