#!/bin/bash
# What the deploy workflow must still say: ./deploy/workflow.test.sh
#
# `actionlint` decides whether `.github/workflows/deploy.yml` is a *valid* workflow. This decides
# whether it is still the workflow EXP-113 settled on, which is a different question and not one
# any linter can answer. The things it holds are the ones that are only wrong once:
#
#   * it deploys a commit CI went green on, and nothing else
#   * two deploys never overlap, and a queued one is never cancelled
#   * the schema is migrated, and the migration is *waited on*, before any app revision changes
#   * the smoke step proves the lockdown by requiring a 403 from the runner, which is not an
#     allowed address — a 200 there means the demo is on the public internet
#   * every secret the two params files read is actually passed, and every one of them arrives
#     from the `production` environment rather than written down here
#
# It reads the workflow as parsed YAML rather than as text, because the order of steps and the
# shape of `on:` are structure, not prose. Nothing here talks to Azure or to GitHub.
#
# Needs jq, and either `yq` (preinstalled on GitHub's runners) or python3 with PyYAML.
set -euo pipefail
cd "$(dirname "$0")/.."

fail=0
note() { echo "  FAIL: $1" >&2; fail=1; }

command -v jq >/dev/null 2>&1 || { echo "  FAIL: jq is not installed" >&2; exit 1; }

wf=.github/workflows/deploy.yml
[ -f "$wf" ] || { echo "  FAIL: $wf does not exist" >&2; exit 1; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

# YAML 1.1 — which PyYAML speaks — reads the unquoted key `on` as the boolean true, while yq reads
# YAML 1.2 and keeps it a string. So whichever parser is here, the key is put back under `on`
# before a single assertion runs.
to_json() {
  if command -v yq >/dev/null 2>&1; then
    yq -o=json '.' "$1"
  elif python3 -c 'import yaml' >/dev/null 2>&1; then
    python3 -c 'import json,sys,yaml; json.dump(yaml.safe_load(open(sys.argv[1])), sys.stdout)' "$1"
  else
    echo "  FAIL: need yq, or python3 with PyYAML, to read $1" >&2
    exit 1
  fi
}

json="$work/deploy.json"
to_json "$wf" | jq 'if has("true") then .on = .true | del(.true) else . end' > "$json"

get() { jq -r "$1" "$json"; }
expect() { # expect <jq filter> <wanted> <what>
  local got
  got=$(get "$1")
  [ "$got" = "$2" ] || note "$3 — expected '$2', the workflow says '$got'"
}

# ------------------------------------------------------------------- what starts it

# A deploy of an untested commit is the whole thing this trigger exists to prevent: `workflow_run`
# on the CI workflow, on main only, and the job itself re-checks the conclusion because
# `types: [completed]` fires on a red run too.
expect '.on.workflow_run.workflows | index("CI") != null' true \
  'the deploy must be triggered by the CI workflow finishing'
expect '.on.workflow_run.types | sort | join(",")' completed 'the workflow_run trigger types'
expect '.on.workflow_run.branches | sort | join(",")' main 'the deploy may only follow a CI run on main'
expect '.on | has("workflow_dispatch")' true 'there must be a manual trigger — it is how a rollback is deployed'
expect '.on.workflow_dispatch.inputs | has("sha")' true 'workflow_dispatch needs a sha input to redeploy an earlier commit'
expect '.on.workflow_dispatch.inputs.sha.required // false' false 'the sha input is optional — no input means the tip of main'

expect '.concurrency.group' deploy-prod 'two deploys must not overlap'
expect '.concurrency."cancel-in-progress"' false \
  'a running deploy must never be cancelled — killed between the migration and the apps is the worst state there is'

expect '.permissions."id-token"' write 'OIDC needs an id-token'
expect '.permissions.contents' read 'the workflow only reads the repository'

# ------------------------------------------------------------------- the one job

jobs=$(get '.jobs | keys | join(" ")')
[ "$jobs" = "deploy" ] || note "the workflow must hold exactly one job named deploy — it has: $jobs"

job='.jobs.deploy'
expect "$job | .environment" production \
  'the job must run in the production environment — that is where the secrets and the OIDC subject are'
guard=$(get "$job | .if // \"\"")
case "$guard" in
  *"workflow_run.conclusion == 'success'"*) ;;
  *) note "the job must refuse a CI run that did not succeed — its if is '$guard'" ;;
esac
case "$guard" in
  *workflow_dispatch*) ;;
  *) note "the guard must still let a manual rollback through — its if is '$guard'" ;;
esac

# ------------------------------------------------------------------- the steps, in order

steps="$job | .steps"
ids=$(get "$steps | map(.id // \"\") | map(select(. != \"\")) | join(\" \")")
index_of() { get "$steps | map(.id // \"\") | index(\"$1\")"; }
for id in target login base images migrate apps smoke; do
  [ "$(index_of "$id")" != "null" ] || note "there is no step with id '$id' — the steps present are: $ids"
done
[ "$fail" -eq 0 ] || { echo "the step ids above are what every later assertion reads; stopping here" >&2; exit 1; }

ordered() { # ordered <earlier id> <later id> <why>
  local a b
  a=$(index_of "$1"); b=$(index_of "$2")
  [ "$a" -lt "$b" ] || note "$3 — '$1' is step $a and '$2' is step $b"
}
ordered login base 'nothing can reach Azure before the OIDC sign-in'
ordered base images 'the registry has to exist before anything is pushed to it'
ordered images migrate 'the migrator runs the image this deploy just pushed'
ordered migrate apps 'the schema must be migrated and waited on before any app revision changes'
ordered apps smoke 'the smoke test reads the deployment that the apps step produced'

run_of() { get "$steps | map(select(.id == \"$1\"))[0].run // \"\""; }
step_of() { get "$steps | map(select(.id == \"$1\"))[0] | tostring"; }
uses_of() { get "$steps | map(select(.id == \"$1\"))[0].uses // \"\""; }
env_of() { get "$steps | map(select(.id == \"$1\"))[0].env // {}"; }
# Comment-only lines are dropped first: a claim about what a step *does* that a sentence about
# what it does can satisfy is not a test. (Only whole-line comments — `${image#etj-}` is code.)
code_of() { run_of "$1" | grep -v '^[[:space:]]*#' || true; }
contains() { # contains <id> <needle> <what>
  case "$(code_of "$1")" in
    *"$2"*) ;;
    *) note "$3 — step '$1' does not mention: $2" ;;
  esac
}

# ------------------------------------------------------------------- the tested commit

# Everything downstream reads one resolved SHA: the commit CI tested, or the one a rollback named.
# A step that checked out `main` instead would deploy whatever has landed since.
mentions() { # mentions <id> <needle> <what> — anywhere in the step, run block or env
  case "$(step_of "$1")" in
    *"$2"*) ;;
    *) note "$3 — step '$1' does not mention: $2" ;;
  esac
}
mentions target 'workflow_run.head_sha' 'the deployed commit must be the one CI tested'
mentions target 'inputs.sha' 'a manual run must be able to name the commit to deploy'
checkout=$(get "$steps | map(select(.uses // \"\" | startswith(\"actions/checkout\")))[0] // {}")
ref=$(jq -r '.with.ref // ""' <<<"$checkout")
case "$ref" in
  *steps.target.outputs.sha*) ;;
  *) note "the checkout must take the resolved SHA, not the default branch — its ref is '$ref'" ;;
esac

# ------------------------------------------------------------------- sign-in, and the two deployments

case "$(uses_of login)" in
  azure/login@*) ;;
  *) note "the sign-in step must be azure/login — it is '$(uses_of login)'" ;;
esac
login_with=$(get "$steps | map(select(.id == \"login\"))[0].with // {}")
for k in client-id tenant-id subscription-id; do
  v=$(jq -r --arg k "$k" '.[$k] // ""' <<<"$login_with")
  case "$v" in
    *secrets.*) ;;
    *) note "azure/login's $k must come from an environment secret — it is '$v'" ;;
  esac
done
# A client secret next to a federated credential means the federation is decoration.
[ "$(jq -r 'has("client-secret")' <<<"$login_with")" = "false" ] \
  || note 'azure/login must sign in by OIDC federation, not with a client secret'

contains base 'infra/base.bicep' 'the base step must deploy the base template'
contains base 'infra/base.bicepparam' 'the base step must use the tracked params file'
contains apps 'infra/apps.bicep' 'the apps step must deploy the apps template'
contains apps 'infra/apps.bicepparam' 'the apps step must use the tracked params file'

# ------------------------------------------------------------------- every secret both files read

# The params files are the authority on what the deployment needs; a secret added there and not
# here fails the *compile* in the pipeline, long after the images are pushed. So the list is read
# out of them rather than copied.
params_vars() { grep -oE "readEnvironmentVariable\('[A-Z0-9_]+'\)" "$1" | grep -oE "'[A-Z0-9_]+'" | tr -d "'" | sort -u; }

for v in $(params_vars infra/base.bicepparam); do
  val=$(env_of base | jq -r --arg v "$v" '.[$v] // ""')
  [ -n "$val" ] || { note "infra/base.bicepparam reads $v and the base step never sets it"; continue; }
  case "$val" in
    *secrets.*) ;;
    *) note "$v must arrive from the production environment's secrets — the base step sets it to '$val'" ;;
  esac
done

for v in $(params_vars infra/apps.bicepparam); do
  val=$(env_of apps | jq -r --arg v "$v" '.[$v] // ""')
  [ -n "$val" ] || { note "infra/apps.bicepparam reads $v and the apps step never sets it"; continue; }
  if [ "$v" = ETJ_IMAGE_TAG ]; then
    # The one that is not a secret, and the one that must not be anything but the deployed SHA.
    case "$val" in
      *steps.target.outputs.sha*) ;;
      *) note "ETJ_IMAGE_TAG must be the resolved SHA — the apps step sets it to '$val'" ;;
    esac
  else
    case "$val" in
      *secrets.*) ;;
      *) note "$v must arrive from the production environment's secrets — the apps step sets it to '$val'" ;;
    esac
  fi
done

# ------------------------------------------------------------------- the six images

for name in edge web mcp agents migrator keycloak; do
  contains images "etj-$name" "every image must be built and pushed — etj-$name is missing"
done
contains images 'docker push' 'the images step must push, not only build'
contains images 'az acr login' 'the push needs a registry login, and it is the deploy identity that has one'
# Tagged by SHA: a rollback is "deploy the previous tag", which a moving tag makes impossible.
case "$(run_of images)" in
  *'steps.target.outputs.sha'*|*'SHA'*) ;;
  *) note 'the images must be tagged with the deployed SHA' ;;
esac
case "$(run_of images)" in
  *:latest*) note 'no image may be pushed as :latest — a tag that moves makes the deployed revision unknowable' ;;
esac

# ------------------------------------------------------------------- the migration, waited on

# The run-and-wait itself lives in a tracked script rather than inline, because the first deploy
# needs it twice (the job does not exist until the apps deployment creates it) and because a human
# applying migrations by hand should take the same path. So the step is checked for calling it,
# and the script for doing what calling it is supposed to mean.
contains migrate 'deploy/migrate.sh' 'the migrate step must run the migration script'
contains migrate 'etj-migrator' 'the migrate step must point the job at the image this deploy pushed'
script=deploy/migrate.sh
[ -x "$script" ] || note "$script must exist and be executable"
script_says() { # script_says <needle> <what>
  grep -q -- "$1" "$script" || note "$2 — $script does not mention: $1"
}
script_says 'az containerapp job start' 'the migrator job must actually be started'
script_says 'az containerapp job execution show' 'the migrator must be waited on, not started and forgotten'
script_says 'Succeeded' 'the wait must test for success rather than for the job having ended'
script_says 'exit 1' 'a failed migration must fail the step, which is what stops the apps deployment'

# The first-deploy second call, and the only thing that keeps it from being a silent no-op twice.
first=$(index_of migrate-first-run)
[ "$first" != "null" ] || note "there is no 'migrate-first-run' step — the first deploy has no migrator job to run before the apps exist"
if [ "$first" != "null" ]; then
  [ "$first" -gt "$(index_of apps)" ] || note 'the first-deploy migration runs after the apps deployment that creates the job'
  guard_first=$(get "$steps | map(select(.id == \"migrate-first-run\"))[0].if // \"\"")
  case "$guard_first" in
    *steps.migrate.outputs.deferred*) ;;
    *) note "the first-deploy migration must only run when the migrate step deferred — its if is '$guard_first'" ;;
  esac
  mentions migrate-first-run MIGRATOR_REQUIRED 'the second attempt must fail on a missing job rather than defer again'
  [ "$(index_of smoke)" -gt "$first" ] || note 'the smoke test must come after the first-deploy migration'
fi

# ------------------------------------------------------------------- the smoke test

contains smoke 'edgeFqdn' 'the smoke test resolves the edge host from the deployment output'
# Not "the step says 403 somewhere" — the step has to *compare* against it. An echo that mentions
# 403 while the test reads `!= 200` passes the loose version of this check and ships the demo to
# the public internet.
code_of smoke | grep -qE '(=|!=|-eq|-ne)[[:space:]]*"?403"?' \
  || note 'the smoke test must compare the edge response against 403 — that is the proof the lockdown is on'
contains smoke 'az containerapp revision list' 'the smoke test must check that every app has a healthy active revision'
# Azure reports an app whose replica count equals its maxReplicas as RunningAtMaxScale, not
# Running — and every app here is min 1 / max 1. Waiting for the bare word "Running" spins for the
# whole retry budget on a perfectly healthy deployment (first deploy, run 37191731314, EXP-127).
code_of smoke | grep -q 'RunningAtMaxScale' \
  || note 'the smoke test must accept RunningAtMaxScale: a min 1 / max 1 app reports that, never plain Running'
for name in etj-edge etj-web etj-mcp etj-agents etj-keycloak; do
  contains smoke "$name" "the smoke test must cover $name"
done

# ------------------------------------------------------------------- nothing written down here

# Secrets reach a shell only through `env:`. Interpolating one into a `run:` block puts it in the
# script GitHub writes to disk and defeats nothing less than the masking.
for id in $ids; do
  case "$(run_of "$id")" in
    *'secrets.'*) note "step '$id' interpolates a secret into its script — pass it through env: instead" ;;
  esac
done

# Any env var anywhere in the file whose name says secret must be an expression, not a value.
literals=$(jq -r '[(.env // {}), (.jobs[].env // {}), (.jobs[].steps[]?.env // {})]
  | map(to_entries) | add // []
  | map(select(.key | test("(?i)secret|password|key|token")))
  | map(select(.value | tostring | test("\\$\\{\\{") | not))
  | map(.key) | join(", ")' "$json")
[ -z "$literals" ] || note "these env vars hold a literal where a secret belongs: $literals"

# Pinned by major version, the way ci.yml does it. An unpinned action is a third party's main
# branch running with this repository's deploy identity.
unpinned=$(jq -r '[.jobs[].steps[]? | .uses // empty | select(test("@v[0-9]+$") | not)] | join(", ")' "$json")
[ -z "$unpinned" ] || note "these actions are not pinned to a major version: $unpinned"

[ "$fail" -eq 0 ] || exit 1
echo "deploy workflow ok (tested commit, no overlap, migration waited on, lockdown smoke-tested)"
