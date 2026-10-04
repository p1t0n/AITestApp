#!/bin/bash
# Run the database migration and wait for it: ./deploy/migrate.sh
#
# The migrator is a manual-trigger Container Apps job (infra/apps.bicep) because the schema has to
# move on a release, not on every app restart, and because Postgres is private — nothing outside
# the Container Apps environment can reach it. So "apply the migrations" is: point the job at the
# image this release built, start it, and *wait*. Waiting is the whole point: a deploy that starts
# the job and walks on would roll new app revisions out over a schema that may have failed to move.
#
# The deploy workflow calls this twice at most (.github/workflows/deploy.yml):
#
#   * before the apps deployment, which is where it belongs — a failure here stops the release
#     with every app still on its previous revision;
#   * again after it, and only on the very first deploy, when the job did not exist yet because
#     `apps.bicep` is what creates it. That call sets MIGRATOR_REQUIRED=1, so a job that is still
#     missing is a failure rather than another shrug.
#
# A human can run it too: `RESOURCE_GROUP=rg-experttojob-app ./deploy/migrate.sh` with az signed
# in, leaving MIGRATOR_IMAGE unset to run whatever image the job already carries.
#
#   RESOURCE_GROUP     required
#   MIGRATOR_JOB       defaults to etj-migrator
#   MIGRATOR_IMAGE     optional; when set, the job is repointed at it before the run
#   MIGRATOR_REQUIRED  set to 1 to fail when the job does not exist
#   GITHUB_OUTPUT      when set, `deferred=true` is written there for the workflow to read
set -euo pipefail

JOB=${MIGRATOR_JOB:-etj-migrator}
: "${RESOURCE_GROUP:?RESOURCE_GROUP is required}"

# 30 min replica timeout on the job itself (infra/apps.bicep); this outlives it on purpose, so a
# job that is killed by its own timeout is reported as the Failed it is rather than as this script
# giving up first.
DEADLINE_S=${MIGRATOR_TIMEOUT_S:-2400}
POLL_S=${MIGRATOR_POLL_S:-10}

if ! az containerapp job show --resource-group "$RESOURCE_GROUP" --name "$JOB" -o none 2>/dev/null; then
  if [ "${MIGRATOR_REQUIRED:-0}" = 1 ]; then
    echo "the migrator job $JOB does not exist in $RESOURCE_GROUP" >&2
    exit 1
  fi
  echo "First deploy: the job $JOB does not exist yet — infra/apps.bicep creates it, and the"
  echo "workflow runs this script again once it does."
  [ -z "${GITHUB_OUTPUT:-}" ] || echo 'deferred=true' >> "$GITHUB_OUTPUT"
  exit 0
fi

if [ -n "${MIGRATOR_IMAGE:-}" ]; then
  echo "pointing $JOB at $MIGRATOR_IMAGE"
  az containerapp job update --resource-group "$RESOURCE_GROUP" --name "$JOB" \
    --image "$MIGRATOR_IMAGE" --only-show-errors -o none
fi

# `job start` answers with the execution it created. Older CLI builds have answered with an empty
# body, so the name is read back from the job's own execution list when that happens — starting a
# run and then watching the wrong one would report a success that never ran.
execution=$(az containerapp job start --resource-group "$RESOURCE_GROUP" --name "$JOB" \
  --query name -o tsv 2>/dev/null || true)
if [ -z "$execution" ] || [ "$execution" = None ]; then
  execution=$(az containerapp job execution list --resource-group "$RESOURCE_GROUP" --name "$JOB" \
    --query "sort_by([], &properties.startTime)[-1].name" -o tsv)
fi
[ -n "$execution" ] || { echo "could not determine which execution of $JOB was started" >&2; exit 1; }
echo "started $execution"

waited=0
while :; do
  status=$(az containerapp job execution show --resource-group "$RESOURCE_GROUP" --name "$JOB" \
    --job-execution-name "$execution" --query properties.status -o tsv)
  case "$status" in
    Succeeded)
      echo "$execution: Succeeded after ${waited}s"
      exit 0
      ;;
    Failed|Degraded|Stopped)
      echo "$execution: $status after ${waited}s — the schema did not move, so nothing else will" >&2
      echo "logs: az containerapp job logs show -g $RESOURCE_GROUP -n $JOB --container migrator" >&2
      exit 1
      ;;
  esac
  if [ "$waited" -ge "$DEADLINE_S" ]; then
    echo "$execution: still '$status' after ${waited}s — giving up" >&2
    exit 1
  fi
  sleep "$POLL_S"
  waited=$((waited + POLL_S))
done
