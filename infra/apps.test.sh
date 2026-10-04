#!/bin/bash
# What the apps deployment must still say: ./infra/apps.test.sh
#
# Same shape as `base.test.sh`, one layer up: compile with the linter (a warning is a failure),
# then read the compiled ARM JSON back and hold it to the decisions that are not preferences.
#
# The decisions here are all of the "gets it wrong once and the demo is on the public internet, or
# minting tokens nobody can validate" kind (EXP-111, EXP-112, EXP-114):
#
#   * exactly one app is reachable from outside, it is the edge, and exactly one address may reach it
#   * no secret ever appears as a plain `value:` env var — only as a `secretRef`
#   * the passkey relying-party id is computed from the environment's own default domain
#   * Keycloak's issuer and the two hosts that validate against it are one string, not three
#   * all eight agent client secrets reach both halves — Agents *and* Keycloak — from one source
#
# Nothing here talks to Azure. It needs `az` (which is what CI uses) or a standalone bicep binary
# named by BICEP=; and jq.
set -euo pipefail
cd "$(dirname "$0")/.."

fail=0
note() { echo "  FAIL: $1" >&2; fail=1; }

command -v jq >/dev/null 2>&1 || { echo "  FAIL: jq is not installed" >&2; exit 1; }

if [ -n "${BICEP:-}" ]; then
  bicep() { "$BICEP" "$@"; }
elif command -v az >/dev/null 2>&1; then
  bicep() {
    local cmd=$1; shift
    local input=$1; shift
    az bicep "$cmd" --file "$input" "$@"
  }
else
  echo "  FAIL: need the Azure CLI on PATH, or BICEP=<path to a bicep binary>" >&2
  exit 1
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# ---------------------------------------------------------------- compile, and mind the warnings

if ! bicep build infra/apps.bicep --outfile "$work/apps.json" 2>"$work/build.err"; then
  cat "$work/build.err" >&2
  echo "  FAIL: infra/apps.bicep does not compile" >&2
  exit 1
fi
diagnostics() { grep -E ' : (Warning|Error) ' "$1" || true; }
if [ -n "$(diagnostics "$work/build.err")" ]; then
  diagnostics "$work/build.err" >&2
  note "infra/apps.bicep compiled with warnings — the bar is zero"
fi

# Every secret this deployment carries comes out of the environment, exactly as `base` does it. A
# throwaway value compiles; nothing set does not, so there is no path from "forgot a secret" to a
# Keycloak realm whose client secret is a string printed in this public repository (EXP-117).
secrets=(
  ETJ_IMAGE_TAG=0000000000000000000000000000000000000000
  # A documentation address (RFC 5737 TEST-NET-3) — the real one comes from the production
  # environment's ALLOWED_IP variable and is deliberately not in this public repository (EXP-131).
  ETJ_ALLOWED_IP=203.0.113.7/32
  ETJ_PG_ADMIN_PASSWORD=test-only-not-a-real-password
  ETJ_JWT_SIGNING_KEY=test-only-not-a-real-signing-key-32-bytes
  ETJ_KEYCLOAK_ADMIN_PASSWORD=test-only-not-a-real-password
  ETJ_AZURE_FOUNDRY_API_KEY=test-only-not-a-real-key
  AGENT_ROSTER_QA_SECRET=test-only-not-a-real-secret-1
  AGENT_CV_TAILORING_SECRET=test-only-not-a-real-secret-2
  AGENT_MATCH_SECRET=test-only-not-a-real-secret-3
  AGENT_SHORTLIST_SECRET=test-only-not-a-real-secret-4
  AGENT_INTERVIEW_KIT_SECRET=test-only-not-a-real-secret-5
  AGENT_BENCH_REPORT_SECRET=test-only-not-a-real-secret-6
  AGENT_RESUME_INGESTION_SECRET=test-only-not-a-real-secret-7
  AGENT_ROSTER_SCAN_SECRET=test-only-not-a-real-secret-8
)

# Exported inside a subshell rather than passed as a command prefix, because `bicep` is a shell
# function here: `env -u` could not run it, and a prefixed assignment in front of a function call
# is not reliably scoped to it.
export_secrets() {
  local pair
  for pair in "${secrets[@]}"; do
    export "${pair?}"
  done
}

if ! ( export_secrets; bicep build-params infra/apps.bicepparam --outfile "$work/apps.params.json" ) 2>"$work/params.err"; then
  cat "$work/params.err" >&2
  note "infra/apps.bicepparam does not compile even with every secret set"
fi
if [ -n "$(diagnostics "$work/params.err")" ]; then
  diagnostics "$work/params.err" >&2
  note "infra/apps.bicepparam compiled with warnings — the bar is zero"
fi

# One run per secret: drop exactly that variable and the params file must refuse to compile. A loop
# rather than one spot-check, because "every one of them is required" is the claim, and twelve of
# thirteen is the shape Keycloak's realm import fails open on.
for pair in "${secrets[@]}"; do
  v=${pair%%=*}
  if ( export_secrets; unset "$v"; bicep build-params infra/apps.bicepparam --outfile "$work/leak.json" ) >/dev/null 2>&1; then
    note "infra/apps.bicepparam compiles with no $v set — that secret has a default somewhere"
  fi
done

# A tracked params file in a public repo.
if grep -nEi "param +[A-Za-z]*([Ss]ecret|[Pp]assword|[Kk]ey)[A-Za-z]* *= *'" infra/apps.bicepparam >&2; then
  note 'infra/apps.bicepparam assigns a literal secret'
fi

arm="$work/apps.json"
get() { jq -r "$1" "$arm"; }
expect() { # expect <jq filter> <wanted> <what>
  local got
  got=$(get "$1")
  [ "$got" = "$2" ] || note "$3 — expected '$2', compiled to '$got'"
}

app() { echo ".resources[] | select(.type == \"Microsoft.App/containerApps\") | select(.name == \"$1\")"; }
job() { echo ".resources[] | select(.type == \"Microsoft.App/jobs\") | select(.name == \"$1\")"; }
# Both kinds carry one container; `env` and `secrets` read the same way on either.
env_of() { echo "($1) | .properties.template.containers[0].env // []"; }

# ------------------------------------------------------------------- the five apps and the job

names=$(get '[.resources[] | select(.type == "Microsoft.App/containerApps") | .name] | sort | join(" ")')
[ "$names" = "etj-agents etj-edge etj-keycloak etj-mcp etj-web" ] \
  || note "the five container apps must be exactly etj-agents etj-edge etj-keycloak etj-mcp etj-web — they are: $names"

jobs=$(get '[.resources[] | select(.type == "Microsoft.App/jobs") | .name] | sort | join(" ")')
[ "$jobs" = "etj-migrator" ] || note "the migrator must be a Container Apps job named etj-migrator — jobs are: $jobs"

mig=$(job etj-migrator)
expect "$mig | .properties.configuration.triggerType" Manual 'the migrator job is triggered by hand, never on a schedule'
expect "$mig | .properties.configuration.replicaTimeout" 1800 'the migrator job replica timeout (30 min)'
expect "$mig | .properties.configuration.replicaRetryLimit" 0 'the migrator must not retry — a half-applied migration is not improved by running it again'

# ------------------------------------------------------------------- the lockdown

external=$(get '[.resources[] | select(.type == "Microsoft.App/containerApps") | select(.properties.configuration.ingress.external == true) | .name] | sort | join(" ")')
[ "$external" = "etj-edge" ] \
  || note "exactly one app may be externally reachable and it must be etj-edge — external apps: ${external:-none}"

for name in etj-web etj-mcp etj-agents etj-keycloak; do
  expect "$(app "$name") | .properties.configuration.ingress.external" false "$name must take internal ingress only"
done

edge=$(app etj-edge)
expect "$edge | .properties.configuration.ingress.ipSecurityRestrictions | length" 1 \
  'the edge must carry exactly one IP rule — a second Allow widens the door, a Deny next to an Allow is ignored'
expect "$edge | .properties.configuration.ingress.ipSecurityRestrictions[0].action" Allow \
  'the edge IP rule must be an Allow (one Allow denies everything else; a lone Deny allows everything else)'
expect "$edge | .properties.configuration.ingress.ipSecurityRestrictions[0].ipAddressRange" "[parameters('allowedIp')]" \
  'the edge IP rule must be the allowedIp parameter, not a literal'
# No default: the address the demo is opened to is somebody's own, and a default here would put it
# back in a public repository (EXP-131). With none, a missing value fails the compile.
expect '.parameters.allowedIp | has("defaultValue")' false 'allowedIp must have no default — the address is supplied at deploy time'
# Nothing tracked under infra/, deploy/ or .github/ may carry a literal /32 other than a
# documentation address. Matches the shape, not the one address, so it holds for any future one.
leaked=$(grep -rnE '([0-9]{1,3}\.){3}[0-9]{1,3}/32' infra deploy .github 2>/dev/null | grep -vE '203\.0\.113\.[0-9]+/32' | grep -vE 'apps\.test\.sh' || true)
[ -z "$leaked" ] || note "a literal single-address CIDR is tracked (the allowed IP belongs in the ALLOWED_IP variable): $(printf '%s' "$leaked" | head -3 | cut -c1-120)"

# An IP rule on an internal app would be a decoration that reads like a defence.
for name in etj-web etj-mcp etj-agents etj-keycloak; do
  expect "$(app "$name") | (.properties.configuration.ingress.ipSecurityRestrictions // []) | length" 0 \
    "$name is internal, so an IP rule on it means nothing"
done

# Max 1 everywhere (EXP-111 item 6). etj-web is the one where it is a correctness requirement
# rather than a cost one: the passkey challenge cache is in memory, so a second replica fails
# ceremonies at random.
for name in etj-edge etj-web etj-mcp etj-agents etj-keycloak; do
  expect "$(app "$name") | .properties.template.scale.maxReplicas" 1 "$name maxReplicas"
  expect "$(app "$name") | .properties.template.scale.minReplicas" 1 "$name minReplicas"
done

# ------------------------------------------------------------------- secrets never ride as values

# The securestring parameters, by name. Anything that reaches an env var's `value:` carrying one of
# these is a secret in the revision's plain configuration, readable by anyone who can read the app.
mapfile -t secure_params < <(get '.parameters | to_entries[] | select(.value.type == "securestring") | .key')
[ "${#secure_params[@]}" -gt 0 ] || note 'no securestring parameters at all — the secrets cannot be arriving securely'

all_env="$work/all-env.json"
jq '[.resources[]
     | select(.type == "Microsoft.App/containerApps" or .type == "Microsoft.App/jobs")
     | .name as $app
     | (.properties.template.containers // [])[]
     | (.env // [])[]
     | {app: $app, name: .name, value: (.value // null), secretRef: (.secretRef // null)}]' "$arm" > "$all_env"

for p in "${secure_params[@]}"; do
  offenders=$(jq -r --arg p "parameters('$p')" \
    '[.[] | select(.value != null and (.value | tostring | contains($p))) | "\(.app)/\(.name)"] | join(", ")' "$all_env")
  [ -z "$offenders" ] || note "the secure parameter $p reaches a plain env value: $offenders"
done

# The other direction: an env var whose *name* says secret must arrive by secretRef, whatever its
# value happens to look like today.
secretish=$(jq -r '[.[]
  | select(.name | test("(?i)(secret|password|signingkey|apikey|^connectionstrings__)"))
  | select(.secretRef == null) | "\(.app)/\(.name)"] | join(", ")' "$all_env")
[ -z "$secretish" ] || note "these env vars name a secret but do not use secretRef: $secretish"

# And every declared secret must be sourced from a secure parameter rather than written down here.
secrets_json="$work/secrets.json"
jq '[.resources[]
     | select(.type == "Microsoft.App/containerApps" or .type == "Microsoft.App/jobs")
     | .name as $app | (.properties.configuration.secrets // [])[]
     | {app: $app, name: .name, value: (.value | tostring)}]' "$arm" > "$secrets_json"
# `contains` over a list, not one regex: `parameters('x')` is full of regex metacharacters, and a
# pattern that silently matched nothing would have passed this check for every secret in the file.
needles=$(printf "parameters('%s')\n" "${secure_params[@]}" | jq -R . | jq -s .)
literal=$(jq -r --argjson needles "$needles" \
  '[.[] | . as $s | select(any($needles[]; . as $n | ($s.value | contains($n))) | not) | "\($s.app)/\($s.name)"] | join(", ")' \
  "$secrets_json")
[ -z "$literal" ] || note "these container-app secrets are not built from a secure parameter: $literal"

# ------------------------------------------------------------------- the passkey relying party

# `*.azurecontainerapps.io` is a public suffix, so the relying-party id has to be the full edge
# host (EXP-114 item 3) — and it has to be computed, because the environment's default domain
# carries a unique id nobody can know before `base` is deployed.
rp=$(get "$(env_of "$(app etj-web)") | map(select(.name == \"Auth__Passkey__ServerDomain\"))[0].value // \"\"")
case "$rp" in
  *'defaultDomain'*'etj-edge'*|*'etj-edge'*'defaultDomain'*) ;;
  *) note "Auth__Passkey__ServerDomain must be built from the edge app name and the environment's defaultDomain — it is '$rp'" ;;
esac
origin=$(get "$(env_of "$(app etj-web)") | map(select(.name == \"Auth__Passkey__Origins__0\"))[0].value // \"\"")
case "$origin" in
  *https://*'defaultDomain'*) ;;
  *) note "Auth__Passkey__Origins__0 must be the https origin of the same computed edge host — it is '$origin'" ;;
esac

# ------------------------------------------------------------------- one Keycloak issuer, not three

kc_hostname=$(get "$(env_of "$(app etj-keycloak)") | map(select(.name == \"KC_HOSTNAME\"))[0].value // \"\"")
case "$kc_hostname" in
  *'.internal.'*'defaultDomain'*) ;;
  *) note "KC_HOSTNAME must be the internal FQDN built from the environment's defaultDomain — it is '$kc_hostname'" ;;
esac

mcp_authority=$(get "$(env_of "$(app etj-mcp)") | map(select(.name == \"Mcp__Authority\"))[0].value // \"\"")
[ -n "$mcp_authority" ] || note 'the MCP host has no Mcp__Authority'
case "$mcp_authority" in
  *'/realms/expert-to-job'*) ;;
  *) note "Mcp__Authority must name the expert-to-job realm — it is '$mcp_authority'" ;;
esac
# The token Keycloak mints names the issuer Keycloak was told; the MCP host rejects anything else.
# If these two strings can drift, the first thing that notices is a 401 nobody can read.
# Both compile to ARM expressions, so this is containment of the inner expression rather than a
# string prefix: the issuer is KC_HOSTNAME with the realm path appended, and nothing else.
kc_inner=${kc_hostname#\[}; kc_inner=${kc_inner%\]}
case "$mcp_authority" in
  *"$kc_inner"*) ;;
  *) note "Mcp__Authority ('$mcp_authority') is not built on KC_HOSTNAME ('$kc_hostname') — they can drift" ;;
esac
# `Mcp:Resource` is an opaque audience the realm's mapper already carries; overriding it here would
# break the match (EXP-112 item 5).
mcp_resource=$(get "$(env_of "$(app etj-mcp)") | map(select(.name == \"Mcp__Resource\")) | length")
[ "$mcp_resource" = "0" ] || note 'Mcp__Resource must stay at its default — the realm audience mapper is what it has to match'

# ------------------------------------------------------------------- eight secrets, both halves

agents_env=$(env_of "$(app etj-agents)")
kc_env=$(env_of "$(app etj-keycloak)")
secret_value() { # secret_value <app jq> <secret name>  -> the expression behind it
  get "($1) | .properties.configuration.secrets // [] | map(select(.name == \"$2\"))[0].value // \"\"" 2>/dev/null
}

for pair in "roster-qa:ROSTER_QA" "cv-tailoring:CV_TAILORING" "match:MATCH" "shortlist:SHORTLIST" \
            "interview-kit:INTERVIEW_KIT" "bench-report:BENCH_REPORT" "resume-ingestion:RESUME_INGESTION" \
            "roster-scan:ROSTER_SCAN"; do
  key=${pair%%:*}
  var=${pair##*:}

  a_ref=$(get "$agents_env | map(select(.name == \"McpAuth__${key}__ClientSecret\"))[0].secretRef // \"\"")
  [ -n "$a_ref" ] || note "the Agents host has no McpAuth__${key}__ClientSecret secretRef"
  k_ref=$(get "$kc_env | map(select(.name == \"AGENT_${var}_SECRET\"))[0].secretRef // \"\"")
  [ -n "$k_ref" ] || note "the Keycloak container has no AGENT_${var}_SECRET secretRef"

  a_val=$(secret_value "$(app etj-agents)" "$a_ref")
  k_val=$(secret_value "$(app etj-keycloak)" "$k_ref")
  if [ -z "$a_val" ] || [ "$a_val" != "$k_val" ]; then
    note "agent '$key': Agents reads '$a_val' and Keycloak reads '$k_val' — both halves must come from one parameter"
  fi

  # The authority each agent's token request goes to is the same realm the MCP host validates.
  a_auth=$(get "$agents_env | map(select(.name == \"McpAuth__${key}__Authority\"))[0].value // \"\"")
  [ "$a_auth" = "$mcp_authority" ] \
    || note "agent '$key' asks '$a_auth' for a token while the MCP host validates against '$mcp_authority'"
done

# ------------------------------------------------------------------- the rest of the wiring

expect "$agents_env | map(select(.name == \"McpServer__BaseUrl\"))[0].value" 'http://etj-mcp' \
  'the Agents host reaches the roster through the MCP app inside the environment'
expect "$(env_of "$edge") | map(select(.name == \"WEB_UPSTREAM\"))[0].value" 'etj-web' 'the edge proxies /api to the Web host'
expect "$(env_of "$edge") | map(select(.name == \"AGENTS_UPSTREAM\"))[0].value" 'etj-agents' 'the edge proxies /agents to the Agents host'

for name in etj-web etj-mcp etj-agents; do
  expect "$(env_of "$(app "$name")") | map(select(.name == \"ASPNETCORE_ENVIRONMENT\"))[0].value" Production \
    "$name must run as Production — the startup guards that refuse a missing key only run there"
done
expect "$(env_of "$mig") | map(select(.name == \"ASPNETCORE_ENVIRONMENT\"))[0].value" Production \
  'the migrator must run as Production'

# Both AI provider names on every host that reads them: the Web host derives the Art. 15 recipient
# from them while MCP embeds and Agents chats, so a host on a stale value names a company that
# receives nothing (EXP-61).
for name in etj-web etj-mcp etj-agents; do
  for key in Ai__Chat__Provider Ai__Embeddings__Provider; do
    expect "$(env_of "$(app "$name")") | map(select(.name == \"$key\"))[0].value" AzureFoundry "$name $key"
  done
done

# Pull by managed identity, never by a registry password.
for name in etj-edge etj-web etj-mcp etj-agents etj-keycloak; do
  expect "$(app "$name") | [.properties.configuration.registries[]? | select(has(\"passwordSecretRef\"))] | length" 0 \
    "$name must not pull with a registry password"
  expect "$(app "$name") | [.properties.configuration.registries[]? | select(.identity != null)] | length" 1 \
    "$name must pull with the user-assigned identity"
done
expect "$mig | [.properties.configuration.registries[]? | select(has(\"passwordSecretRef\"))] | length" 0 \
  'the migrator job must not pull with a registry password'

# Every image is the tag the pipeline passes, out of the registry base created.
for pair in "etj-edge:etj-edge" "etj-web:etj-web" "etj-mcp:etj-mcp" "etj-agents:etj-agents" "etj-keycloak:etj-keycloak"; do
  name=${pair%%:*}
  image=$(get "$(app "$name") | .properties.template.containers[0].image")
  case "$image" in
    *"/${name}:"*|*"/${name}',"*) ;;
    *) note "$name must run the ${name} image — it runs '$image'" ;;
  esac
  case "$image" in
    *"parameters('imageTag')"*) ;;
    *) note "$name must be pinned to the imageTag parameter — it runs '$image'" ;;
  esac
done
mig_image=$(get "$mig | .properties.template.containers[0].image")
case "$mig_image" in
  *'etj-migrator'*"parameters('imageTag')"*) ;;
  *) note "the migrator job must run the etj-migrator image at the imageTag parameter — it runs '$mig_image'" ;;
esac

# Sizing is what the $60 budget was costed against (infra/README.md): Keycloak is the JVM, the
# rest are 0.25 vCPU / 0.5 GiB.
expect "$(app etj-keycloak) | .properties.template.containers[0].resources.cpu" "[json('0.5')]" 'the Keycloak container is the one that gets half a core'
expect "$(app etj-keycloak) | .properties.template.containers[0].resources.memory" '1Gi' 'the Keycloak container memory'
for name in etj-edge etj-web etj-mcp etj-agents; do
  expect "$(app "$name") | .properties.template.containers[0].resources.cpu" "[json('0.25')]" "$name cpu"
  expect "$(app "$name") | .properties.template.containers[0].resources.memory" '0.5Gi' "$name memory"
done

# Probes have to point at endpoints that exist. /health and /alive are ServiceDefaults' and are
# anonymous (api/ServiceDefaults/Extensions.cs); Keycloak's live on the management port 9000 and
# never on 8080 (manuals/keycloak-prod-realm.md).
for name in etj-web etj-mcp etj-agents; do
  probes=$(get "$(app "$name") | [.properties.template.containers[0].probes[]? | \"\(.type) \(.httpGet.path):\(.httpGet.port)\"] | sort | join(\"; \")")
  case "$probes" in
    *'/health:8080'*) ;;
    *) note "$name has no readiness probe on /health:8080 — probes are: ${probes:-none}" ;;
  esac
  case "$probes" in
    *'/alive:8080'*) ;;
    *) note "$name has no liveness probe on /alive:8080 — probes are: ${probes:-none}" ;;
  esac
done
kc_probe_ports=$(get "$(app etj-keycloak) | [.properties.template.containers[0].probes[]? | .httpGet.port] | unique | join(\",\")")
[ "$kc_probe_ports" = "9000" ] \
  || note "Keycloak's health endpoints exist only on the management port 9000 — its probes point at $kc_probe_ports"

# ------------------------------------------------------------------- outputs

[ "$(get '.outputs | has("edgeFqdn")')" = "true" ] || note 'output edgeFqdn is missing — the deploy workflow smoke-tests it'
# An output is deployment history, which is readable by anyone with Reader on the group.
leaky=$(get '.outputs | to_entries | map(select(.value.type == "securestring")) | map(.key) | join(", ")')
[ -z "$leaky" ] || note "these outputs are secrets: $leaky"

[ "$fail" -eq 0 ] || exit 1
echo "apps deployment ok (edge lockdown, five apps, migrator job, secrets by reference)"
