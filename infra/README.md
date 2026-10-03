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

## Order of use

1. **Bootstrap, once, by hand** (EXP-119) — the resource group, the OIDC federated identity scoped
   to it, and the GitHub `production` environment that holds the secrets. Not in this directory.
2. **`base`** — `az deployment group create -g rg-experttojob-app -f infra/base.bicep -p infra/base.bicepparam`,
   with `ETJ_PG_ADMIN_PASSWORD` exported. Takes roughly 15 minutes; the flexible server is the slow
   part. Run once, then only to change the base.
3. **Push the images** (EXP-115) to `experttojobacr` — `base` created the registry, so it has to
   come first, and the apps cannot start without the tags.
4. **`apps`** (EXP-121) — the five container apps, the migrator job and the IP lockdown, taking
   `base`'s outputs (`acrLoginServer`, `environmentId`, `environmentDefaultDomain`, `postgresFqdn`,
   `appsIdentityId`) as its inputs.

Steps 2–4 are what the deploy workflow (EXP-122) automates; 2 and 3 are also the only order that
works by hand.

A `.bicepparam` file cannot be combined with inline `--parameters` overrides, which is why the one
secret arrives as an environment variable rather than on the command line. It has no default: with
`ETJ_PG_ADMIN_PASSWORD` unset, the params file fails to compile, so there is no path from "forgot
the secret" to a deployed server.

## What `base` creates, and what it costs

Monthly, USD, `swedencentral`. "Floor" is every app at the Container Apps **idle** rate; "ceiling"
is every app at the **active** rate 24/7. Both already net off the free monthly grant (180,000
vCPU-s / 360,000 GiB-s). The spread is wide because which rate applies is a property of how busy
the demo is, not of this file.

| Resource | Name | What it is | Floor | Ceiling |
|---|---|---|---|---|
| Virtual network | `vnet-experttojob` | `10.20.0.0/16`; `snet-cae-infra` /23 for the environment, `snet-postgres` /24 delegated to the database | $0 | $0 |
| Private DNS zone | `pg-experttojob-swc.private.postgres.database.azure.com` | linked to the VNet; it is also the server's FQDN | not priced in EXP-109 | — |
| Log Analytics | `log-experttojob` | 30-day retention, capped at 1 GB/day ingestion | included free tier | $2.99/GB over 5 GB |
| Container Apps environment | `cae-experttojob` | **Consumption-only**, VNet-integrated, logs to the workspace | $0 | $0 |
| — the five apps' compute | `etj-edge\|web\|mcp\|agents\|keycloak` (EXP-121) | all min 1 / max 1 | $33.86 | $112.86 |
| PostgreSQL flexible server | `pg-experttojob-swc` | Burstable B1ms, PG17, 32 GiB, private access only, `azure.extensions=VECTOR`, databases `experttojob` + `keycloak` | $18.91 | $18.91 |
| Container registry | `experttojobacr` | Basic, admin user off, 10 GiB included | $5.07 | $5.07 |
| Managed identity | `id-etj-apps` | `AcrPull` on the registry; how the apps pull without a password | $0 | $0 |
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
BICEP=/path/to/bicep ./infra/base.test.sh  # or a standalone bicep binary
```

CI runs the same script in the `Infra (bicep)` job, only when something under `infra/` changed.
It needs `jq`, and no Azure credentials — nothing here talks to Azure.
