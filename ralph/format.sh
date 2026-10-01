#!/bin/bash
# Translate `claude --output-format stream-json` into readable terminal lines.
#
# sbx hands us the agent's raw event stream on stdout; unformatted that is a wall of
# single-line JSON. This renders it. Non-JSON lines (sbx's own chatter, docker noise)
# pass through, minus the terminal-control sequences sbx emits (window title, clear
# screen), so nothing is silently swallowed and the scrollback is not wiped.
#
# One line per tool call, naming what it acts on; a tool result prints only when it
# failed, with its first line. Successful results are implied by the next call.
#
# Usage:  sbx run ... | ./ralph/format.sh

jq -Rr --unbuffered '
  def esc(c): "\u001b[\(c)m";
  def clip(n): if length > n then .[0:n] + "…" else . end;
  def oneline: split("\n") | map(select(length > 0)) | first // "";
  def subject:
    if .name == "Bash" then (.input.description // (.input.command | oneline))
    elif (.input.file_path? // null) then .input.file_path
    elif (.input.pattern? // null) then .input.pattern
    elif (.input.id? // .input.issueId? // null) then (.input.id // .input.issueId)
    elif (.input.query? // null) then .input.query
    elif (.input.team? // null) then [.input.team, .input.state] | map(select(.)) | join(" / ")
    else "" end
    | tostring | oneline | clip(100);

  # sbx wraps the stream in terminal control (OSC window title, clear screen, cursor show)
  # and the pty adds \r. Strip all of it; the only colour on screen is the colour added here.
  gsub("\u001b\\][^\u0007\u001b]*(\u0007|\u001b\\\\)|\u001b\\[[0-9;?]*[A-Za-z]|\u001b\\\\|\r"; "") as $line
  | ($line | try (sub("^[^{]*(?=\\{\"type\")"; "") | fromjson) catch null) as $e
  | if $e == null then
      ($line | select(length > 0))
    elif $e.type == "system" and $e.subtype == "init" then
      "\(esc(2))> session \($e.session_id[0:8]) | \($e.model // "?") | \($e.tools | length) tools\(esc(0))"
    elif $e.type == "assistant" then
      ( $e.message.content[]?
        | if .type == "text" then
            (.text | select(length > 0) | "\n" + .)
          elif .type == "tool_use" then
            "\(esc(36))  * \(.name | sub("^mcp__[^_]+(__|_)"; ""))\(esc(0))\(esc(2)) \(subject)\(esc(0))"
          else empty end )
    elif $e.type == "user" then
      ( $e.message.content[]?
        | select(.type == "tool_result" and (.is_error // false))
        | (.content | if type == "array" then map(.text? // "") | join("\n") else tostring end) as $c
        | "\(esc(31))    <- \($c | split("\n") | map(select(length > 0 and (startswith("Exit code") | not))) | first // $c | clip(160))\(esc(0))" )
    elif $e.type == "result" then
      "\n\(esc(1))# \($e.subtype) | \($e.num_turns // 0) turns | \((($e.duration_ms // 0) / 1000) | floor)s"
      + (if $e.total_cost_usd then " | $\(($e.total_cost_usd * 100 | round) / 100)" else "" end)
      + esc(0)
    else empty end
  # `sbx run` holds the terminal in raw mode, where a bare \n moves down without returning to
  # column 0, so every line staircases. End each line, including line breaks inside the
  # agent text, with \r\n; a cooked terminal ignores the extra \r. (No apostrophes in this
  # program: it sits inside a single-quoted shell string.)
  | gsub("\n"; "\r\n") + "\r"
'
