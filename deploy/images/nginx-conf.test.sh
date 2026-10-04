#!/bin/bash
# What the edge's nginx config must still say: ./deploy/images/nginx-conf.test.sh  (exit 0 = pass)
#
# Renders deploy/images/nginx/default.conf.template exactly the way the edge container will — the
# nginx image's own envsubst step, same filter, same variable names — then asserts the directives
# that are load-bearing rather than stylistic, and finishes with `nginx -t` on the result so a
# config that greps clean but will not parse still fails here.
#
# Needs Docker. The nginx tag must match the one deploy/images/edge.Dockerfile runs.
set -euo pipefail
cd "$(dirname "$0")/../.."

# Both come out of the Dockerfile rather than being restated here, so this renders what the edge
# actually renders — a filter changed there has to answer to this test rather than to nobody.
NGINX_IMAGE=$(sed -n 's/^FROM \(nginx:[^ ]*\).*/\1/p' deploy/images/edge.Dockerfile | head -1)
[ -n "$NGINX_IMAGE" ] || { echo "could not read the nginx tag out of edge.Dockerfile" >&2; exit 1; }
ENVSUBST_FILTER=$(sed -n "s/^ *ENV NGINX_ENVSUBST_FILTER='\([^']*\)'.*/\1/p" deploy/images/edge.Dockerfile | head -1)

# Literal IPs, not hostnames: nginx resolves a proxy_pass host at config-load time, so `nginx -t`
# against `etj-web` would fail for want of DNS rather than for want of a correct config.
WEB_UPSTREAM=127.0.0.1:9101
AGENTS_UPSTREAM=127.0.0.1:9102

rendered=$(docker run --rm \
  -v "$PWD/deploy/images/nginx/default.conf.template:/etc/nginx/templates/default.conf.template:ro" \
  -e "NGINX_ENVSUBST_FILTER=$ENVSUBST_FILTER" \
  -e "WEB_UPSTREAM=$WEB_UPSTREAM" \
  -e "AGENTS_UPSTREAM=$AGENTS_UPSTREAM" \
  -e "host=DECOYHOST" -e "uri=DECOYURI" -e "scheme=DECOYSCHEME" \
  "$NGINX_IMAGE" \
  sh -c '/docker-entrypoint.sh nginx -t >/tmp/nginx-t.log 2>&1 || { cat /tmp/nginx-t.log >&2; exit 1; }
         cat /etc/nginx/conf.d/default.conf')

fail=0
note() { echo "  FAIL: $1" >&2; fail=1; }

# The body of one `location <prefix> { … }` block, by brace depth — so a directive in a sibling
# location can never satisfy an assertion meant for this one.
block() {
  printf '%s\n' "$rendered" | awk -v want="location $1 {" '
    index($0, want) { inside = 1 }
    inside { depth += gsub(/\{/, "{") - gsub(/\}/, "}"); print; if (depth == 0) exit }'
}

has() { # has <block-text> <literal> <description>
  case "$1" in
    *"$2"*) ;;
    *) note "$3 — expected to find: $2" ;;
  esac
}

api=$(block /api/)
agents=$(block /agents/)
root=$(block /)

[ -n "$api" ] || note "no 'location /api/' block"
[ -n "$agents" ] || note "no 'location /agents/' block"

# --- /api/ : the prefix survives the hop -------------------------------------------------------
# The SPA's axios baseURL is /api and the Web controllers are routed at api/... , and the Vite dev
# proxy rewrites nothing (web/vite.config.ts). The edge must not rewrite either.
has "$api" "proxy_pass http://$WEB_UPSTREAM/api/;" "/api/ must pass the /api prefix through"
has "$api" 'proxy_set_header Host $host;' "/api/ must forward Host"
has "$api" 'proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;' "/api/ must forward X-Forwarded-For"
has "$api" 'proxy_set_header X-Forwarded-Proto $scheme;' "/api/ must forward X-Forwarded-Proto"

# --- /agents/ : SSE survives the hop -----------------------------------------------------------
# POST /agents/staffing streams Server-Sent Events with a 15 s keep-alive, and a run can outlive
# any default read timeout. Buffering is what silently turns that stream into one lump at the end.
has "$agents" "proxy_pass http://$AGENTS_UPSTREAM/agents/;" "/agents/ must pass the /agents prefix through"
has "$agents" 'proxy_http_version 1.1;' "/agents/ must speak HTTP/1.1 upstream"
has "$agents" 'proxy_buffering off;' "/agents/ must not buffer the response"
has "$agents" 'proxy_cache off;' "/agents/ must not cache the response"
has "$agents" 'proxy_read_timeout 3600s;' "/agents/ must outlive a long staffing run"
has "$agents" 'proxy_set_header Connection "";' "/agents/ must clear Connection so keep-alive holds"
has "$agents" 'X-Accel-Buffering no' "/agents/ must tell the ingress in front of it not to buffer"
has "$agents" 'proxy_set_header Host $host;' "/agents/ must forward Host"
has "$agents" 'proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;' "/agents/ must forward X-Forwarded-For"
has "$agents" 'proxy_set_header X-Forwarded-Proto $scheme;' "/agents/ must forward X-Forwarded-Proto"

# --- the SPA itself ----------------------------------------------------------------------------
# react-router owns every path that is not /api or /agents, so an unknown path is index.html, not
# a 404 — a deep link pasted into a fresh tab has to boot the app.
has "$root" 'try_files $uri $uri/ /index.html;' "/ must fall back to index.html"
has "$rendered" 'listen 8080;' "the edge must listen on 8080"

# --- envsubst did its job, and only its job ----------------------------------------------------
case "$rendered" in
  *'${WEB_UPSTREAM}'*|*'${AGENTS_UPSTREAM}'*) note "an upstream placeholder survived rendering" ;;
esac
# What the filter buys: nginx's runtime variables share a namespace with the container's
# environment, and envsubst left to itself substitutes every variable that happens to be defined.
# The decoy env vars above are named exactly like three of them, so a render with no filter (or a
# loose one) swallows them here — and would still pass nginx -t out in the deployment.
for var in '$host' '$scheme' '$proxy_add_x_forwarded_for' '$uri'; do
  has "$rendered" "$var" "envsubst ate nginx's own variable $var"
done
case "$rendered" in
  *DECOY*) note "envsubst substituted a variable it was not asked to — NGINX_ENVSUBST_FILTER is not holding" ;;
esac

if [ "$fail" -ne 0 ]; then
  echo "--- rendered config ---" >&2
  printf '%s\n' "$rendered" >&2
  exit 1
fi
echo "nginx edge config ok (rendered and nginx -t under $NGINX_IMAGE)"
