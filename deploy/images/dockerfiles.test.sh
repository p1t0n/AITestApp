#!/bin/bash
# What the five image definitions must still agree on: ./deploy/images/dockerfiles.test.sh
#
# The four .NET hosts share one recipe written four times — one file each, so a build command
# needs no arguments. This is what stops those four from drifting apart, and what pins the few
# values the rest of the deployment reads back out of them. No Docker needed.
set -euo pipefail
cd "$(dirname "$0")/../.."

fail=0
note() { echo "  FAIL: $1" >&2; fail=1; }

# Which project each image publishes, and what its entry assembly is called.
DOTNET_IMAGES="web:api/Web/ExpertToJob.Web.csproj:ExpertToJob.Web
mcp:api/Mcp/ExpertToJob.Mcp.csproj:ExpertToJob.Mcp
agents:api/Agents/ExpertToJob.Agents.csproj:ExpertToJob.Agents
migrator:api/Migrator/ExpertToJob.Migrator.csproj:ExpertToJob.Migrator"

# The SDK image has to carry the SDK global.json pins, or the image is built on a different
# compiler than everything else in this repo. `11.0` is the RC channel tag; global.json rolls
# forward to the latest patch, which is exactly what that tag tracks.
sdk_tag=$(sed -n 's|^FROM mcr.microsoft.com/dotnet/sdk:\([^ ]*\).*|\1|p' deploy/images/web.Dockerfile 2>/dev/null | head -1)
[ -n "$sdk_tag" ] || { echo "  FAIL: could not read the SDK tag out of deploy/images/web.Dockerfile" >&2; exit 1; }
pinned=$(sed -n 's/.*"version": "\([^"]*\)".*/\1/p' global.json | head -1)
case "$pinned" in
  "${sdk_tag%%-*}"*) ;;
  *) note "global.json pins SDK $pinned but the images build on mcr.microsoft.com/dotnet/sdk:$sdk_tag" ;;
esac

for entry in $DOTNET_IMAGES; do
  name=${entry%%:*}; rest=${entry#*:}; project=${rest%%:*}; assembly=${rest##*:}
  file="deploy/images/$name.Dockerfile"
  [ -f "$file" ] || { note "missing $file"; continue; }
  body=$(cat "$file")
  check() { case "$body" in *"$1"*) ;; *) note "$file — expected to find: $1" ;; esac; }

  check "FROM mcr.microsoft.com/dotnet/sdk:$sdk_tag AS build"
  check "FROM mcr.microsoft.com/dotnet/aspnet:$sdk_tag"
  check "dotnet publish $project"
  check "ENTRYPOINT [\"dotnet\", \"$assembly.dll\"]"
  # Non-root, and said out loud: `app` ships in the runtime image, but an image that never names
  # a USER runs as root and nothing downstream would notice.
  check "USER app"
  # The one port. Container Apps targets a single port per app and the edge's upstreams assume it.
  check "ASPNETCORE_HTTP_PORTS=8080"
  # Production is the effective environment in a container; appsettings.Development.json is kept
  # out of the build context entirely (.dockerignore), and this is the other half of that rule.
  check "ASPNETCORE_ENVIRONMENT=Production"

  # Instructions only — the comments above are free to explain why it is excluded.
  if printf '%s\n' "$body" | grep -v '^[[:space:]]*#' | grep -q 'appsettings\.Development\.json'; then
    note "$file copies appsettings.Development.json — it must never reach an image"
  fi
done

# The edge, whose defaults the apps deployment resolves to real Container Apps names.
edge=$(cat deploy/images/edge.Dockerfile)
edge_check() { case "$edge" in *"$1"*) ;; *) note "edge.Dockerfile — expected to find: $1" ;; esac; }
edge_check "FROM node:22-alpine AS spa"
edge_check "WEB_UPSTREAM=etj-web"
edge_check "AGENTS_UPSTREAM=etj-agents"
edge_check "NGINX_ENVSUBST_FILTER='^(WEB_UPSTREAM|AGENTS_UPSTREAM)\$'"
edge_check "USER nginx"
edge_check "EXPOSE 8080"

# .dockerignore is load-bearing twice over: it keeps the context small, and it is what guarantees
# a developer's local appsettings.Development.json (which in a dirty tree has held a real key)
# cannot be copied into a layer.
ignore=$(cat .dockerignore)
for pattern in '**/bin/' '**/obj/' '**/node_modules/' '.git' '/docs' '**/appsettings.Development.json'; do
  case "$ignore" in
    *"$pattern"*) ;;
    *) note ".dockerignore must exclude $pattern" ;;
  esac
done

[ "$fail" -eq 0 ] || exit 1
echo "image definitions ok (4 .NET hosts + edge)"
