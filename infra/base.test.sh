#!/bin/bash
# What the base deployment must still say: ./infra/base.test.sh
#
# Two halves. First the compile has to be clean — `bicep build` runs the linter, and a warning
# here is a failure, because nothing downstream reads warnings. Then the compiled ARM JSON is read
# back and held to the handful of decisions that are not preferences: a database with no public
# surface, the SKUs the $60 budget was costed against, a registry that has no admin password to
# leak, and an environment with no workload profile to bill.
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
  # `az bicep build` takes --file where the standalone CLI takes a positional argument; this
  # wrapper hides that one difference so the rest of the script reads the same either way.
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

if ! bicep build infra/base.bicep --outfile "$work/base.json" 2>"$work/build.err"; then
  cat "$work/build.err" >&2
  echo "  FAIL: infra/base.bicep does not compile" >&2
  exit 1
fi
# Only compiler diagnostics count: `az bicep` also writes upgrade notices to stderr, and an
# "a new Bicep release is available" line is not a reason to red a pull request.
diagnostics() { grep -E ' : (Warning|Error) ' "$1" || true; }
if [ -n "$(diagnostics "$work/build.err")" ]; then
  diagnostics "$work/build.err" >&2
  note "infra/base.bicep compiled with warnings — the bar is zero"
fi

# The params file is only honest if it cannot be compiled without the secret. Both directions are
# asserted: a throwaway password compiles, and no password at all does not.
if ! ETJ_PG_ADMIN_PASSWORD='test-only-not-a-real-password' \
     bicep build-params infra/base.bicepparam --outfile "$work/base.params.json" 2>"$work/params.err"; then
  cat "$work/params.err" >&2
  note "infra/base.bicepparam does not compile even with ETJ_PG_ADMIN_PASSWORD set"
fi
if [ -n "$(diagnostics "$work/params.err")" ]; then
  diagnostics "$work/params.err" >&2
  note "infra/base.bicepparam compiled with warnings — the bar is zero"
fi
# A subshell, not `env -u`: `bicep` is a shell function here and `env` could not run it, which
# would have made this check quietly pass whatever the params file said.
if ( unset ETJ_PG_ADMIN_PASSWORD; bicep build-params infra/base.bicepparam --outfile "$work/leak.json" >/dev/null 2>&1 ); then
  note "infra/base.bicepparam compiles with no ETJ_PG_ADMIN_PASSWORD set — the admin password has a default somewhere"
fi

# A tracked params file in a public repo: the password must be read from the environment, and no
# literal may sit next to it.
if ! grep -q "readEnvironmentVariable('ETJ_PG_ADMIN_PASSWORD')" infra/base.bicepparam; then
  note "infra/base.bicepparam must take postgresAdminPassword from readEnvironmentVariable('ETJ_PG_ADMIN_PASSWORD')"
fi
if grep -nEi "param +postgresAdminPassword *= *'" infra/base.bicepparam >&2; then
  note "infra/base.bicepparam assigns a literal admin password"
fi

arm="$work/base.json"
get() { jq -r "$1" "$arm"; }
expect() { # expect <jq filter> <wanted> <what>
  local got
  got=$(get "$1")
  [ "$got" = "$2" ] || note "$3 — expected '$2', compiled to '$got'"
}

# ------------------------------------------------------------------------------- PostgreSQL

pg='.resources[] | select(.type == "Microsoft.DBforPostgreSQL/flexibleServers")'
expect "[$pg] | length" 1 'exactly one PostgreSQL flexible server'
expect "$pg | .properties.network.publicNetworkAccess" Disabled 'Postgres must have no public network access'
expect "$pg | .sku.name" Standard_B1ms 'Postgres SKU'
expect "$pg | .sku.tier" Burstable 'Postgres tier'
expect "$pg | .properties.version" 17 'Postgres major version'
expect "$pg | .properties.storage.storageSizeGB" 32 'Postgres storage (the Burstable minimum)'
expect "$pg | .properties.network.delegatedSubnetResourceId | length > 0" true 'Postgres must sit in the delegated subnet'
expect "$pg | .properties.network.privateDnsZoneArmResourceId | length > 0" true 'Postgres must be wired to the private DNS zone'
expect "$pg | .properties.administratorLoginPassword" "[parameters('postgresAdminPassword')]" 'Postgres password must come from the secure parameter'
expect '.parameters.postgresAdminPassword.type' securestring 'postgresAdminPassword must be a secure parameter'
expect '.parameters.postgresAdminPassword | has("defaultValue")' false 'postgresAdminPassword must have no default'
# A firewall rule on a VNet-integrated server is a contradiction; on a public one it is the leak.
expect '[.resources[] | select(.type == "Microsoft.DBforPostgreSQL/flexibleServers/firewallRules")] | length' 0 'no Postgres firewall rules'

ext=".resources[] | select(.type == \"Microsoft.DBforPostgreSQL/flexibleServers/configurations\") | select(.name | contains(\"azure.extensions\"))"
expect "[$ext] | length" 1 'the azure.extensions server parameter must be set'
if ! get "$ext | .properties.value" | tr '[:lower:]' '[:upper:]' | grep -qw VECTOR; then
  note "azure.extensions must include VECTOR — compiled to '$(get "$ext | .properties.value")'"
fi

dbs=$(get '[.resources[] | select(.type == "Microsoft.DBforPostgreSQL/flexibleServers/databases") | .name] | sort | join(" ")')
case "$dbs" in
  *experttojob*) ;;
  *) note "the experttojob database is missing (databases: $dbs)" ;;
esac
case "$dbs" in
  *keycloak*) ;;
  *) note "the keycloak database is missing (databases: $dbs)" ;;
esac

# ------------------------------------------------------------------------------- registry

acr='.resources[] | select(.type == "Microsoft.ContainerRegistry/registries")'
expect "[$acr] | length" 1 'exactly one container registry'
expect "$acr | .sku.name" Basic 'ACR SKU'
expect "$acr | .properties.adminUserEnabled" false 'ACR admin user must be off — the apps pull with the managed identity'

# AcrPull for the apps identity, on the registry and nowhere wider.
expect '[.resources[] | select(.type == "Microsoft.Authorization/roleAssignments")] | length' 1 'exactly one role assignment'
ra='.resources[] | select(.type == "Microsoft.Authorization/roleAssignments")'
if ! get "$ra | .properties.roleDefinitionId" | grep -q '7f951dda-4ed3-4680-a7ca-43fe172d538d'; then
  note 'the role assignment must grant AcrPull (7f951dda-4ed3-4680-a7ca-43fe172d538d)'
fi

# ------------------------------------------------------------------------------- environment

env_res='.resources[] | select(.type == "Microsoft.App/managedEnvironments")'
expect "[$env_res] | length" 1 'exactly one Container Apps environment'
# Consumption-only means the property is absent. A workload-profiles environment bills an
# Environment Management Hour meter that would eat the budget on its own (EXP-109's research
# note, §1, on branch research/azure-deploy-cost-ip).
profiles=$(get "$env_res | (.properties.workloadProfiles // []) | map(.workloadProfileType) | unique | join(\",\")")
case "$profiles" in
  ''|'Consumption') ;;
  *) note "the environment must be Consumption-only — it declares workload profiles: $profiles" ;;
esac
expect "$env_res | .properties.vnetConfiguration.infrastructureSubnetId | length > 0" true 'the environment must be VNet-integrated'
expect "$env_res | .properties.appLogsConfiguration.destination" log-analytics 'the environment must log to the workspace'

# ------------------------------------------------------------------------------- network

expect '[.resources[] | select(.type == "Microsoft.Network/virtualNetworks")] | length' 1 'exactly one virtual network'
cae_prefix=$(get '.parameters.containerAppsSubnetPrefix.defaultValue')
# /23 or larger — the Consumption-only floor.
# https://learn.microsoft.com/en-us/azure/container-apps/networking
case "${cae_prefix##*/}" in
  2[0-3]|1[0-9]|[0-9]) ;;
  *) note "the Container Apps infrastructure subnet is $cae_prefix — a Consumption-only environment needs /23 or larger" ;;
esac
subnets="$work/subnets.json"
jq '.resources[] | select(.type == "Microsoft.Network/virtualNetworks") | .properties.subnets' "$arm" > "$subnets"
# Subnet *names* compile to variable references, so each one is picked out by the parameter its
# address prefix comes from.
cae_delegations=$(jq -r '.[] | select(.properties.addressPrefix | contains("containerAppsSubnetPrefix")) | (.properties.delegations // []) | length' "$subnets")
[ "$cae_delegations" = "0" ] || note "the Container Apps subnet must carry no delegation — it has $cae_delegations"
pg_delegation=$(jq -r '.[] | select(.properties.addressPrefix | contains("postgresSubnetPrefix")) | (.properties.delegations // [])[0].properties.serviceName // ""' "$subnets")
[ "$pg_delegation" = "Microsoft.DBforPostgreSQL/flexibleServers" ] \
  || note "the Postgres subnet must be delegated to Microsoft.DBforPostgreSQL/flexibleServers — it is '$pg_delegation'"

zone=$(get '.resources[] | select(.type == "Microsoft.Network/privateDnsZones") | .name')
expect '[.resources[] | select(.type == "Microsoft.Network/privateDnsZones/virtualNetworkLinks")] | length' 1 'the private DNS zone must be linked to the VNet'
[ -n "$zone" ] || note 'no private DNS zone for Postgres'

# ------------------------------------------------------------------------------- logs and money

ws='.resources[] | select(.type == "Microsoft.OperationalInsights/workspaces")'
expect "$ws | .properties.retentionInDays" 30 'log retention'
expect "$ws | .properties.workspaceCapping.dailyQuotaGb" 1 'the daily ingestion cap keeps an unbounded log bill off the budget'

budget='.resources[] | select(.type == "Microsoft.Consumption/budgets")'
expect "[$budget] | length" 1 'exactly one budget'
expect "$budget | .properties.amount" 60 'the monthly budget'
expect "$budget | .properties.timeGrain" Monthly 'the budget period'
alerts=$(get "$budget | .properties.notifications | to_entries | map(\"\(.value.thresholdType) \(.value.operator) \(.value.threshold)\") | sort | join(\"; \")")
[ "$alerts" = "Actual GreaterThan 80; Forecasted GreaterThan 100" ] \
  || note "budget alerts must be actual>80% and forecast>100% — they are: $alerts"
if ! get "$budget | .properties.notifications | to_entries | map(.value.contactEmails | length) | min" | grep -qv '^0$'; then
  note 'every budget notification needs a contact email'
fi

# ------------------------------------------------------------------------------- outputs

for name in acrLoginServer environmentId environmentDefaultDomain postgresFqdn appsIdentityId; do
  # The deploy workflow reads these; infra/apps.bicep reaches the same resources by name instead.
  [ "$(get ".outputs | has(\"$name\")")" = "true" ] || note "output $name is missing"
done

# Nothing public that was not asked for.
expect '[.resources[] | select(.type == "Microsoft.Network/publicIPAddresses")] | length' 0 'no public IP addresses'
expect '[.resources[] | select(.type | startswith("Microsoft.KeyVault"))] | length' 0 'no Key Vault (EXP-108: secrets come from the pipeline)'

[ "$fail" -eq 0 ] || exit 1
echo "base infrastructure ok (vnet, postgres, acr, environment, logs, budget)"
