# infra/ — the Azure deployment, as Bicep

Resource group `rg-experttojob-app`, region `swedencentral`, one environment. The decisions behind
every line are EXP-108 and its grilling tickets; the prices come from EXP-109's research note —
`manuals/research-azure-deploy-cost-and-ip-restrictions.md` on branch `research/azure-deploy-cost-ip`,
which is not merged to main (retail API read 2026-10-03, USD pay-as-you-go, `swedencentral`).

Nothing in this directory deploys itself. Every `az` write is a human's, with Roman confirming what
is created and what it costs.

| File | What it is |
|---|---|
| `base.bicep` | the base: network, database, registry, logs, Container Apps environment, budget |
| `base.bicepparam` | its parameters. No secret: the admin password is read from `ETJ_PG_ADMIN_PASSWORD` |
| `base.test.sh` | compiles both, with the linter, and asserts the non-negotiables out of the ARM JSON |
| `apps.bicep` | the five container apps, the migrator job and the IP lockdown |
| `apps.bicepparam` | their parameters. No secret: every one is read from the environment |
| `apps.test.sh` | the same again for `apps` — the lockdown, the probes, and "no secret rides as a value" |

## Order of use

1. **Bootstrap, once, by hand** (EXP-119) — the resource group, the OIDC federated identity scoped
   to it, and the GitHub `production` environment that holds the secrets. Not in this directory.
2. **`base`** — `az deployment group create -g rg-experttojob-app -f infra/base.bicep -p infra/base.bicepparam`,
   with `ETJ_PG_ADMIN_PASSWORD` exported. Takes roughly 15 minutes; the flexible server is the slow
   part. Run once, then only to change the base.
3. **Push the images** (EXP-115) to `experttojobacr` — `base` created the registry, so it has to
   come first, and the apps cannot start without the tags.
4. **`apps`** — `az deployment group create -g rg-experttojob-app -f infra/apps.bicep -p infra/apps.bicepparam`,
   with every variable in the table below exported. It reaches `base`'s resources **by name**
   rather than taking its outputs as parameters, so nothing has to be threaded between the two
   deployments — which also means the environment's default domain, the one value nobody can know
   before step 2, is read live every time instead of being copied into a pipeline variable that
   can go stale.
5. **Run the migrator job once** — `az containerapp job start -g rg-experttojob-app -n etj-migrator`.
   It is a manual-trigger job precisely so that redeploying step 4 never touches the schema on its
   own. The apps do not wait for it: no host applies migrations any more.

Steps 2–5 are what the deploy workflow automates; by hand, the order above is the only
one that works — the registry has to exist before the images, the images before the apps, and the
schema before anything reads it.

A `.bicepparam` file cannot be combined with inline `--parameters` overrides, which is why every
secret arrives as an environment variable rather than on the command line. None of them has a
default: with any one unset, the params file fails to **compile**, so there is no path from
"forgot a secret" to a deployment that half worked.

## Deploying

`.github/workflows/deploy.yml` is steps 2–5 above, run for you. It is **not** triggered by a push:
it fires on `workflow_run` of the `CI` workflow, only when that run concluded `success` on `main`,
and it checks out the exact SHA CI tested. A push-triggered deploy would race the tests it depends
on. `concurrency: deploy-prod` with `cancel-in-progress: false` means two deploys never overlap and
a running one is never killed — the state between the migration and the apps deployment is the one
place an interrupted release really hurts. There is no approval reviewer (EXP-113 item 2): the gate
is that CI was green.

The order inside the job, which is the order above with one wrinkle:

1. `azure/login` by OIDC federation — no client secret anywhere.
2. `base` — `az deployment group create`, idempotent, every time. It is how a base change ships.
3. the six images, built and pushed to `experttojobacr` tagged with the **full git SHA**. Never
   `latest`.
4. `deploy/migrate.sh` — repoints the `etj-migrator` job at the new image, starts it, and **waits**.
   A failure here ends the release with every app still on its previous revision.
5. `apps` with `imageTag=<sha>` and every secret from the `production` environment.
6. the smoke test.

The wrinkle is the very first deploy, where step 4 has no job to run: `apps.bicep` is what creates
`etj-migrator`. The script says so, writes `deferred=true`, and the workflow runs it once more
after step 5 — that second call sets `MIGRATOR_REQUIRED=1`, so a job that is *still* missing fails
rather than being shrugged at twice. Every deploy after the first takes the normal path.

The same script is what a human runs to apply migrations by hand:
`RESOURCE_GROUP=rg-experttojob-app ./deploy/migrate.sh` with `az` signed in, and
`MIGRATOR_IMAGE=` left unset to run whatever image the job already carries.

### What the `production` environment must hold, and what reads each name

`deploy/github-environment.contract` is the tracked list of names — never values. It mirrors the
environment rather than creating it; the real inventory is `gh secret list --env production` and
`gh variable list --env production`, and when the two disagree it is the tracked file that is
wrong. It exists because GitHub expands an **undefined secret to the empty string**: a wrong name
in `${{ secrets.X }}` does not fail at the reference, it surfaces three steps later as a Bicep
length error, on a release that has already built and pushed six images (EXP-125).
`deploy/workflow.test.sh` holds `deploy.yml` to the list in both directions — a reference to a
name that is not on it fails, and a name on it marked `used` that no step reads fails too.

The GitHub name on the left, the environment variable `deploy.yml` sets from it on the right. They
are the same word often enough that the one that differs is worth reading twice:

| GitHub `production` | Kind | Set in `deploy.yml` as | Read by |
|---|---|---|---|
| `AZURE_CLIENT_ID` | secret | `with: client-id` | `azure/login` |
| `AZURE_TENANT_ID` | secret | `with: tenant-id` | `azure/login` |
| `AZURE_SUBSCRIPTION_ID` | secret | `with: subscription-id` | `azure/login` |
| `ETJ_PG_ADMIN_PASSWORD` | secret | `ETJ_PG_ADMIN_PASSWORD` | both params files — one login serves both databases |
| `ETJ_JWT_SIGNING_KEY` | secret | `ETJ_JWT_SIGNING_KEY` | `apps.bicepparam` |
| `ETJ_KEYCLOAK_ADMIN_PASSWORD` | secret | `ETJ_KEYCLOAK_ADMIN_PASSWORD` | `apps.bicepparam` |
| `ETJ_AZURE_FOUNDRY_API_KEY` | secret | `ETJ_AZURE_FOUNDRY_API_KEY` | `apps.bicepparam` |
| `AGENT_ROSTER_QA_SECRET` … `AGENT_ROSTER_SCAN_SECRET` (8) | secret | the same eight names | `apps.bicepparam`; each reaches Keycloak *and* the Agents host |
| `ALLOWED_IP` | variable | **`ETJ_ALLOWED_IP`** | `apps.bicepparam` → `allowedIp` (EXP-131) |
| `SEED_ADMIN_EMAIL` | variable | — | nothing, on purpose (below) |
| `BUDGET_ALERT_EMAIL` | variable | — | nothing, on purpose (below) |

`ALLOWED_IP` is the one name that changes across the colon, and it is the one the test pins by
name rather than by shape.

`SEED_ADMIN_EMAIL` and `BUDGET_ALERT_EMAIL` exist in the environment and are deliberately left
unwired (EXP-132). Both addresses are the project mailbox `expert2job@hotmail.com`, and both are
already tracked literals — `seedAdministratorEmail` in `apps.bicepparam`, `budgetAlertEmail` in
`base.bicepparam`. Wiring them would buy no privacy, since the mailbox is the project's rather
than a person's and is already in the repository, while adding two more values to the bootstrap
contract that no test here can see are missing. That is the trade `deployPrincipalObjectId` is a
literal for: a wrong literal is a wrong-looking line in a diff, a wrong environment value is a
release that fails on the fifth step. They are listed as `unread` instead, and the test fails if
a later edit reads one without moving the line. Deleting them from the environment is a human's
call; until then they are inert, not pending.

### What the smoke test proves, and what it cannot

It resolves `edgeFqdn` from the apps deployment's outputs, curls it **from the runner**, and
requires **403**. The runner is not the allowed address, so a 403 is the lockdown working and a 200 is
the demo sitting on the public internet. Then it waits for each of the five apps to report an
active revision in `Running` with at least one replica.

What it cannot do is prove the *allow* path — no GitHub runner has the allowed address. That one
check is a human's, once, from that address (EXP-123).

### Rolling back

Images are tagged by SHA and kept, so a rollback is a redeploy of an earlier one: **Actions → Deploy
→ Run workflow**, with `sha` set to the commit to go back to. Leaving `sha` empty deploys the tip of
`main`. Nothing rolls back automatically, and **migrations do not roll back at all** — they are
forward-only, so a rollback past a schema change needs a new migration, not an older image.

### What the one-time bootstrap must already have created (EXP-119)

Without all three, the workflow cannot succeed — which is the intended failure, not a bug:

* the resource group `rg-experttojob-app`; the deploy identity cannot create its own;
* one Entra app registration with a federated credential for
  `repo:p1t0n/AITestApp:environment:production`, holding **Contributor** and **Role Based Access
  Control Administrator** on that resource group only — RBAC admin because `base.bicep` grants
  `AcrPull` to the apps identity and `AcrPush` to this app registration — and nothing on the
  Foundry resource group. Its **object id** goes into `base.bicepparam` as
  `deployPrincipalObjectId`;
* the GitHub `production` environment, restricted to `main`, holding every name in
  `deploy/github-environment.contract` — the three `AZURE_*` ids, the four `ETJ_*` secrets, the
  eight `AGENT_*_SECRET`, and the `ALLOWED_IP` variable.

### Checking the workflow without deploying

```bash
./deploy/workflow.test.sh   # needs jq, and yq or python3 with PyYAML
```

It reads `deploy.yml` as parsed YAML and holds it to the decisions above — the tested commit, the
concurrency rules, the migration waited on *before* the apps move, the 403, and that every variable
the two `.bicepparam` files read is passed and comes from a secret. The list of those variables is
read out of the params files rather than copied, so a secret added there and forgotten here is a
red CI job instead of a compile failure halfway through a release. It also checks the *other* side
of each of those lines — every `secrets.X` and `vars.X` the workflow names, against
`deploy/github-environment.contract` — which is the half a typo actually hides in. CI runs it next
to `actionlint` in the `Workflows (actionlint)` job.

## What `apps` needs, and what each parameter is

Everything in the first table is a secret and lives in the GitHub `production` environment; nothing
in it is ever written down in this repository. Everything in the second is a decision with a
default, overridable on the command line.

| Environment variable | Parameter | What it is |
|---|---|---|
| `ETJ_IMAGE_TAG` | `imageTag` | the git SHA all six images were built and pushed under. Not `latest`: a rollback is "deploy the previous tag", and a tag that moves makes the deployed revision unknowable |
| `ETJ_PG_ADMIN_PASSWORD` | `postgresAdminPassword` | the same password `base` created the server with — one login serves both databases |
| `ETJ_JWT_SIGNING_KEY` | `jwtSigningKey` | the session JWT key. The Web host issues with it, Web and Agents both validate with it |
| `ETJ_KEYCLOAK_ADMIN_PASSWORD` | `keycloakAdminPassword` | Keycloak's bootstrap admin. The console is not browser-reachable; this is for `az containerapp exec` |
| `ETJ_AZURE_FOUNDRY_API_KEY` | `aiFoundryApiKey` | the Foundry **key2** (local development keeps key1). Prefixed so a deploy cannot silently pick up whichever key the operator had exported |
| `AGENT_ROSTER_QA_SECRET` … `AGENT_ROSTER_SCAN_SECRET` (8) | `agentRosterQaSecret` … | the eight Keycloak client secrets. **Each one reaches two containers**: Keycloak resolves its realm-import placeholder from it, and the Agents host reads it as `McpAuth:<agent>:ClientSecret` |

| Parameter | Default | What it is |
|---|---|---|
| `location` | `swedencentral` | must be the region `base` was deployed into |
| `environmentName` | `cae-experttojob` | `base`'s Container Apps environment, read as an existing resource |
| `registryName` | `experttojobacr` | `base`'s registry; supplies the login server every image name is built from |
| `appsIdentityName` | `id-etj-apps` | `base`'s user-assigned identity. It holds `AcrPull`, and it is how every app pulls — there is no registry password |
| `postgresServerName` | `pg-experttojob-swc` | `base`'s flexible server; supplies the private FQDN both connection strings are built from |
| `allowedIp` | the `ALLOWED_IP` variable of the GitHub `production` environment, as `ETJ_ALLOWED_IP` | the one address allowed to reach the edge; no default and no tracked literal, because the repository is public (EXP-131) |
| `postgresAdminLogin` | `etjadmin` | matches `base` |
| `aiFoundryEndpoint` | the `experttojob-openai-swc` v1 endpoint | shared by chat and embeddings |
| `keycloakAdminUsername` | `admin` | |
| `seedAdministratorEmail` | `expert2job@hotmail.com` | the account made staff on first sign-in (EXP-112) |

Output: `edgeFqdn` — the edge app's own ingress FQDN, which is what the deploy workflow smoke-tests.

### The one manual grant that has to be removed before the next `base` deploy

`base.bicep` grants the deploy identity **AcrPush** on `experttojobacr`. The same grant already
exists in Azure, made by hand on 2026-10-04 to get the first release unstuck (EXP-128): Contributor
on the resource group is a control-plane role and cannot push a layer, so `az acr login` failed with
*"Unable to authenticate using AAD or admin login credentials"*
([run 37190558603](https://github.com/p1t0n/AITestApp/actions/runs/37190558603)).

Azure enforces role-assignment uniqueness on the **(scope, principal, role)** triple, not on the
assignment's name. The template names its assignment deterministically — `guid(registry.id,
deployPrincipalObjectId, '8311e382-…')` — which makes **its own** redeploys idempotent: the same
input gives the same name, so the second deployment is a no-op update rather than a second
assignment. It does nothing for an assignment created outside the template: `az role assignment
create` picks a random GUID, so the template's PUT is a *different name for the same triple*, and
Azure answers **409 `RoleAssignmentExists`** ([Azure/bicep#19936](https://github.com/Azure/bicep/issues/19936),
[Troubleshoot Azure RBAC](https://learn.microsoft.com/en-us/azure/role-based-access-control/troubleshooting?tabs=bicep)).

Bicep has no "adopt it if it is already there" for role assignments, so this is not something the
template can tolerate. **Before the next `base` deployment, delete the manual assignment:**

```bash
az role assignment delete \
  --assignee 822c48bb-d583-4772-8f56-3c21ab4ed426 \
  --role AcrPush \
  --scope "$(az acr show -n experttojobacr -g rg-experttojob-app --query id -o tsv)"
```

There is no window to worry about: the deployment recreates it in the same run, before the images
step that needs it. Afterwards the grant is the template's, and every later deploy is a no-op on it.

### Two things worth knowing before the first deploy

**The Web host is given no AI key.** It constructs neither provider; it reads `Ai__Chat__Provider`
and `Ai__Embeddings__Provider` only to name the right recipient on the privacy page (EXP-61), and
all three hosts have to agree on those two values. The key goes to the two hosts that actually call
a model — MCP (embeddings) and Agents (chat) — and nowhere else.

**One string is the Keycloak issuer.** `KC_HOSTNAME`, `Mcp__Authority` and all eight
`McpAuth__<agent>__Authority` values are computed from a single expression, because a token minted
under one issuer and validated against another is a 401 with nothing in the logs to explain it.
`apps.test.sh` asserts they cannot drift apart.

## What `base` creates, and what it costs

Monthly, USD, `swedencentral`. "Floor" is every app at the Container Apps **idle** rate; "ceiling"
is every app at the **active** rate 24/7. Both already net off the free monthly grant (180,000
vCPU-s / 360,000 GiB-s). The spread is wide because which rate applies is a property of how busy
the demo is, not of this file.

| Resource | Name | What it is | Floor | Ceiling |
|---|---|---|---|---|
| Virtual network | `vnet-experttojob` | `10.20.0.0/16`; `snet-cae-infra` /23 delegated to `Microsoft.App/environments`, `snet-postgres` /24 delegated to the database | $0 | $0 |
| Private DNS zone | `pg-experttojob-swc.private.postgres.database.azure.com` | linked to the VNet; it is also the server's FQDN | not priced in EXP-109 | — |
| Log Analytics | `log-experttojob` | 30-day retention, capped at 1 GB/day ingestion | included free tier | $2.99/GB over 5 GB |
| Container Apps environment | `cae-experttojob` | **Consumption-only**, VNet-integrated, logs to the workspace | $0 | $0 |
| — the five apps' compute | `etj-edge\|web\|mcp\|agents\|keycloak` (`apps.bicep`) | all min 1 / max 1; 0.25 vCPU / 0.5 GiB each except Keycloak at 0.5 / 1 GiB — the split these figures were costed against | $33.86 | $112.86 |
| — the migrator job | `etj-migrator` (`apps.bicep`) | 0.5 vCPU / 1 GiB, manual trigger, a minute or two per release | negligible | negligible |
| PostgreSQL flexible server | `pg-experttojob-swc` | Burstable B1ms, PG17, 32 GiB, private access only, `azure.extensions=VECTOR`, databases `experttojob` + `keycloak` | $18.91 | $18.91 |
| Container registry | `experttojobacr` | Basic, admin user off, 10 GiB included | $5.07 | $5.07 |
| Managed identity | `id-etj-apps` | `AcrPull` on the registry; how the apps pull without a password | $0 | $0 |
| Role assignments | — | `AcrPull` for `id-etj-apps` and `AcrPush` for the deploy identity, both on the registry only | $0 | $0 |
| Budget | `budget-experttojob` | $60/month on the resource group, alerts at 80% actual and 100% forecast | $0 | $0 |
| **Total** | | | **~$57.84** | **~$136.84** |

No public IP, no Key Vault, no custom domain, no Front Door. The only externally reachable thing is
the edge app, which the apps deployment locks to one `/32`.

Two numbers to watch on the first invoice, both flagged as unverified in EXP-109:

* the `Environment Management Hour` meter ($0.13/h = $94.90/month). Microsoft's billing page says a
  Consumption-only environment pays none of it. If it bills anyway, it is larger than everything
  else here put together — check Cost Analysis the day after the first deploy.
* the private DNS zone and anything else the VNet drags in; the research note queried no meter for
  it and that absence is not the same as zero.

## Running the checks

```bash
./infra/base.test.sh                       # uses the Azure CLI's bicep
./infra/apps.test.sh
BICEP=/path/to/bicep ./infra/base.test.sh  # or a standalone bicep binary
```

CI runs both scripts in the `Infra (bicep)` job, only when something under `infra/` changed. They
need `jq`, and no Azure credentials — nothing here talks to Azure.

Both read the **compiled ARM JSON**, which is why `apps.bicep` writes every `env`, `probes`,
`secrets` and `ingress` block out literally instead of building them from variables, `concat()` or
`[for …]` loops: each of those compiles to a single opaque ARM expression string, and a test that
can no longer see inside one passes whatever happens to be in there.
