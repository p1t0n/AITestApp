#!/bin/bash
# Local smoke for the five images: ./deploy/images/smoke.sh  (exit 0 = pass)
#
# Builds all five, then stands the edge up in front of two stub upstreams and asks the three
# questions the edge exists to answer:
#
#   1. Does / serve the built SPA, and does a deep link that react-router owns serve it too?
#   2. Does a call to /api/... arrive at the Web upstream with its prefix intact?
#   3. Does a streamed response on /agents/... reach the client in pieces, or in one lump at the
#      end?
#
# On (3), what each check is worth. nginx-conf.test.sh pins `proxy_buffering off;` as a directive;
# this one times the stream end to end instead. Measured here (2026-10-03, nginx 1.29-alpine):
# flipping that directive back on does NOT make this timing fail — nginx forwards small chunks
# promptly either way when the client reads them. So this is not a test of that directive. It is
# a test that the whole path flows, which is what a later `gzip on`, a buffering default change
# or an upstream that stops flushing would break, and which no amount of reading the config shows.
#
# Needs Docker, and network egress for the base images. Nothing here touches Azure.
set -euo pipefail
cd "$(dirname "$0")/../.."

NET=etj-smoke-net
EDGE_PORT=${EDGE_PORT:-18080}
STUB_DELAY_S=3           # how long the stub holds the stream open after its first chunk
tmp=$(mktemp -d)

cleanup() {
  docker rm -f etj-smoke-edge etj-smoke-web etj-smoke-agents >/dev/null 2>&1 || true
  docker network rm "$NET" >/dev/null 2>&1 || true
  rm -rf "$tmp"
}
trap cleanup EXIT

fail=0
note() { echo "  FAIL: $1" >&2; fail=1; }
step() { echo "==> $1"; }

# --- build ------------------------------------------------------------------------------------
# Forwarded only when set. `npm ci` and `dotnet restore` run inside the build container, which has
# none of the shell's environment, so behind a proxy they reach nothing without this — and the
# Dockerfiles stay free of anything about one machine's network.
build_args=()
for var in HTTP_PROXY HTTPS_PROXY NO_PROXY; do
  [ -n "${!var:-}" ] && build_args+=(--build-arg "$var=${!var}")
done

step "building the five images"
for name in web mcp agents migrator edge; do
  docker build -q "${build_args[@]+"${build_args[@]}"}" \
    -f "deploy/images/$name.Dockerfile" -t "etj-$name:smoke" . >/dev/null
  echo "    etj-$name:smoke"
done

# --- stubs ------------------------------------------------------------------------------------
# Not the real hosts: the real ones need Postgres, Keycloak and a provider key, and none of that
# would tell us anything more about the edge. These answer the two questions the edge's own
# behaviour depends on — what path arrived, and whether chunks flow.
cat > "$tmp/stub.mjs" <<'JS'
import { createServer } from "node:http";
const delayMs = Number(process.env.STUB_DELAY_S ?? 3) * 1000;

createServer(async (req, res) => {
  if (process.env.STUB_ROLE === "agents") {
    // A stand-in for POST /agents/staffing: one SSE frame now, one after a pause. Anything that
    // buffers in between turns this into a single delivery at the end.
    res.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache" });
    res.write("event: first\ndata: {}\n\n");
    setTimeout(() => { res.write("event: last\ndata: {}\n\n"); res.end(); }, delayMs);
    return;
  }
  // The Web stub echoes the path it was actually given, which is the whole assertion: the edge
  // must not strip /api on the way through.
  res.writeHead(200, { "Content-Type": "application/json" });
  res.end(JSON.stringify({ path: req.url }));
}).listen(8080, "0.0.0.0");
JS

step "starting the stub upstreams and the edge"
docker network create "$NET" >/dev/null
for role in web agents; do
  docker run -d --name "etj-smoke-$role" --network "$NET" --network-alias "stub-$role" \
    -e "STUB_ROLE=$role" -e "STUB_DELAY_S=$STUB_DELAY_S" \
    -v "$tmp/stub.mjs:/stub.mjs:ro" node:22-alpine node /stub.mjs >/dev/null
done
docker run -d --name etj-smoke-edge --network "$NET" \
  -e WEB_UPSTREAM=stub-web:8080 -e AGENTS_UPSTREAM=stub-agents:8080 \
  -p "127.0.0.1:$EDGE_PORT:8080" etj-edge:smoke >/dev/null

base="http://127.0.0.1:$EDGE_PORT"
for _ in $(seq 1 50); do
  curl -fsS -o /dev/null "$base/" 2>/dev/null && break
  sleep 0.2
done

# --- 0. non-root ---------------------------------------------------------------------------
whoami_in_edge=$(docker exec etj-smoke-edge id -un)
[ "$whoami_in_edge" = "nginx" ] || note "the edge runs as '$whoami_in_edge', not nginx"

# --- 1. the SPA, and the deep link ----------------------------------------------------------
index=$(curl -fsS "$base/")
case "$index" in
  *'<div id="root">'*) ;;
  *) note "/ did not serve the built SPA" ;;
esac
case "$index" in
  *'/assets/'*) ;;
  *) note "/ served an index.html with no built asset in it — is this the source index.html?" ;;
esac
deep=$(curl -fsS "$base/experts/00000000-0000-0000-0000-000000000000")
[ "$deep" = "$index" ] || note "a deep link did not fall back to index.html"
# The fallback is unconditional, including under /assets/ — a stale client asking for a hashed
# bundle that no longer exists gets index.html, not a 404. Asserted rather than left implied,
# because it is the behaviour behind "Unexpected token '<'" when a deploy has moved on, and a
# future /assets/ { try_files $uri =404; } would be a deliberate change, not a silent one.
asset=$(curl -s -o "$tmp/asset.txt" -w '%{http_code}' "$base/assets/does-not-exist.js")
[ "$asset" = "200" ] && [ "$(cat "$tmp/asset.txt")" = "$index" ] \
  || note "a missing asset returned $asset — the SPA fallback no longer covers /assets/"

# --- 2. /api reaches the Web upstream, prefix intact -----------------------------------------
api=$(curl -fsS "$base/api/health")
case "$api" in
  *'"path":"/api/health"'*) ;;
  *) note "the Web upstream saw $api — the edge did not pass /api/health through unchanged" ;;
esac

# --- 3. /agents streams ------------------------------------------------------------------------
step "timing a streamed response through /agents/"
out="$tmp/stream.txt"
: > "$out"
start=$(date +%s%N)
curl -sN --no-buffer -D "$tmp/head.txt" "$base/agents/staffing" > "$out" &
curl_pid=$!
first_ms=""
for _ in $(seq 1 200); do
  if [ -s "$out" ]; then first_ms=$(( ($(date +%s%N) - start) / 1000000 )); break; fi
  sleep 0.05
done
wait "$curl_pid" || true
total_ms=$(( ($(date +%s%N) - start) / 1000000 ))
body=$(cat "$out")

if [ -z "$first_ms" ]; then
  note "nothing arrived on /agents/ at all"
else
  # The stub holds the stream open for STUB_DELAY_S after its first frame. The first byte reaching
  # us well before that is the whole claim: the response was not held back until the end.
  half=$(( STUB_DELAY_S * 1000 / 2 ))
  echo "    first chunk at ${first_ms}ms, stream closed at ${total_ms}ms (stub holds ${STUB_DELAY_S}s)"
  [ "$first_ms" -lt "$half" ] || note "the first chunk took ${first_ms}ms of a ${STUB_DELAY_S}s stream — it is being held back until the end"
  [ "$total_ms" -ge "$half" ] || note "the stream closed in ${total_ms}ms; the stub cannot have run — check the stub, not the edge"
fi
case "$body" in
  *'event: first'*'event: last'*) ;;
  *) note "the stream body was $body, not the stub's two frames" ;;
esac
# Said on the way out for whatever ingress sits in front of this edge (Container Apps' Envoy).
# Read off the streaming response above rather than a HEAD of its own: the stub holds a HEAD open
# just as long, so a second request would only be a slower way to ask the same question.
grep -qi '^x-accel-buffering: *no' "$tmp/head.txt" \
  || note "the /agents/ response carried no X-Accel-Buffering: no"

[ "$fail" -eq 0 ] || { echo "smoke FAILED" >&2; exit 1; }
echo "smoke ok — SPA served, /api prefix intact, /agents/ streamed through"
