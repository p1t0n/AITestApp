# Research: Container Apps + Postgres Flexible cost and IP-restriction facts (EXP-109)

Parent: EXP-108. Region: `swedencentral`. Prices: Azure Retail Prices API
(`https://prices.azure.com/api/retail/prices`, USD, pay-as-you-go, queried 2026-10-03). Docs: learn.microsoft.com
pages fetched 2026-10-03. Nothing was created or changed in Azure; every call was a read of public docs or the public
price API.

Legend: **[doc]** stated on a Microsoft Learn page; **[price]** read from the retail API; **[calc]** arithmetic on
those; **[unverified]** not stated by a primary source, needs a test or an invoice before anyone relies on it.
730 h/month = 2,628,000 s throughout.

## 1. Container Apps, Consumption plan

| Fact | Value | Source |
|---|---|---|
| Free grant per subscription per calendar month | 180,000 vCPU-s, 360,000 GiB-s, 2M HTTP requests | [doc] billing |
| vCPU active | $0.000024 /s | [price] `Standard vCPU Active Usage` |
| vCPU idle | $0.000003 /s | [price] `Standard vCPU Idle Usage` |
| Memory active and idle | $0.000003 /GiB-s (same for both) | [price] |
| Requests | $0.40 per 1M after the free 2M; only requests from outside the environment count; health probes free | [price] + [doc] billing |
| Scaled to zero | no resource charge | [doc] billing, scale-app |
| Idle rate applies when | min replicas > 0, scaled to that minimum, and the replica uses < 0.01 vCPU, receives < 1,000 B/s and serves no HTTP request | [doc] billing |
| Scale-to-zero defaults | min replicas default 0, max 10; HTTP rule polled every 15 s; scale-in to 0 after a 300 s cool-down | [doc] scale-app |
| Cold-start duration | **not published** as a number in any fetched page; depends on image pull and app start (Keycloak JVM will be the slow one) | [unverified] |
| Allowed Consumption sizes | 0.25 vCPU/0.5 GiB up to 4 vCPU/8 GiB in fixed pairs (0.5/1.0, 1.0/2.0 ...) | [doc] containers |

### Does the Consumption-only setup need a VNet? Is the environment free?

- No VNet is required. By default the environment uses an Azure-managed network, "publicly accessible over the internet";
  supplying your own VNet is optional and immutable after creation. [doc] networking
- The default environment type is **workload profiles** (supports Consumption and Dedicated plans). "Consumption only" is
  now labelled **legacy** (max 2 vCPU/4 GiB per app, no UDR/NAT gateway, `/23` subnet if a VNet is used, vs `/27` for workload
  profiles). Use a workload-profiles environment and add only the built-in Consumption profile. [doc] environment, networking, containers
- Environment cost: legacy Consumption-only "no cost associated with the Container Apps environment". Workload-profiles:
  a management fee applies only when a Dedicated profile exists, and the "Management (hour)" meter also covers private
  endpoint and planned maintenance. [doc] environment, billing; pricing page
- **Risk flag.** The retail API lists a new meter `Environment | Environment Management Hour` at $0.13/h, effective
  2026-09-01 (alongside `Environment Private Endpoint` and `Environment Planned Maintenance Hour`, same price). The billing
  page (updated 2026-03) predates it and says Consumption users pay none of this. If that meter ever bills a plain
  Consumption environment the cost is $0.13 x 730 = **$94.90/month**, which would flip the comparison against the VM.
  [calc] **[unverified]**: confirm on the first invoice or in the pricing calculator before committing.

### Min-replica cost, monthly [calc]

Keycloak: 0.5 vCPU / 1 GiB, always on. Four small .NET apps (Web, MCP, Agents, plus one more): 0.25 vCPU / 0.5 GiB each, min 1.
Total 1.5 vCPU, 3 GiB. The Migrator is a one-shot job, not counted.

| Scenario | Gross | After free grant |
|---|---|---|
| Keycloak always on, all replicas idle-rate | $11.83 | $10.21 |
| Keycloak always on, active rate (24/7 busy) | $39.42 | $34.02 |
| 4 .NET apps always on, idle-rate | $23.65 | (included in total below) |
| All 5 apps min 1, all idle-rate (floor) | $35.48 | **$33.86** |
| All 5 apps min 1, all active-rate 24/7 (ceiling) | $118.26 | **$112.86** |
| Keycloak min 1 + 4 .NET apps min 0, apps active 10% of month | $19.71 | $18.09 |

The grant is subscription-wide and the docs do not say at which rate it is consumed; the table nets it at the rate of the
scenario (the difference is under $4). A JVM Keycloak that runs background jobs may not stay under 0.01 vCPU, so expect
it to sit nearer the active rate than the floor.

## 2. Ingress IP restrictions (`ipSecurityRestrictions`)

All [doc] ip-restrictions unless noted.

- Set per container app on its ingress (portal Networking > Ingress, or `az containerapp ingress access-restriction set|remove|list`).
- Rule = name, description, `ipAddressRange` (IPv4 CIDR only, e.g. `10.200.10.2/32`), action Allow or Deny.
- **All rules on an app must be one type**: allow rules or deny rules, never mixed. With no rules, all inbound traffic is allowed.
  With allow rules, everything not listed is denied (portal wording: "Allow traffic from IPs configured below, deny all other traffic").
- Blocked clients get `RBAC: Access Denied`.
- Rule name and action cannot be changed on update; omitting `--description` deletes it.
- IPv6 is not accepted.
- `X-Forwarded-For` carries only the rightmost client IP from Azure; anything else must be validated by the app. [doc] ingress-overview

**Interaction with internal app-to-app calls: not documented.** No fetched page says whether `ipSecurityRestrictions` is
evaluated for traffic from another app in the same environment, nor what source IP the proxy sees. What is documented:
calls to another app's FQDN "are first sent to the edge ingress proxy", while calls by app name go direct (peer-to-peer
page, which also states peer-to-peer encryption is off by default); in both cases traffic "never leaves the environment".
[doc] connect-apps, ingress-environment-configuration. So an allow-list on an external app that other apps call by FQDN
**may block them**. **[unverified]**: needs one throwaway-environment test (allow-list set, then call by FQDN, then by
`http://<app>`). Safe design until tested: restrict only the public-facing app(s), and make internal callers use the
internal-ingress app on its own FQDN with no restriction on it.

### Request timeout and SSE

- HTTP ingress: "Request time out is 240 seconds"; supports HTTP/1.1, HTTP/2, WebSocket, gRPC; HTTPS TLS 1.2/1.3 terminated
  at the ingress; port 80 redirects to 443. [doc] ingress-overview
- A configurable idle request timeout (4 to 30 min, default 4) exists only with **premium ingress**, which needs a
  dedicated D4-D32 workload profile (not Consumption, not shared, minimum two nodes, billed at that profile's rate). So it is
  not a free fix. [doc] ingress-environment-configuration
- The docs do not mention SSE by name. Treat any SSE stream that can outlive 240 s as cut by the proxy: the client must
  reconnect (EventSource does by default) or the server must send periodic data. **[unverified]** whether 240 s is a total
  cap or an idle cap for streaming responses; test with a stream that sends a byte every 30 s.

## 3. Internal ingress

All [doc] connect-apps, ingress-overview.

- `internal` ingress FQDN: `<app>.internal.<ENV_UNIQUE_ID>.<region>.azurecontainerapps.io` (external drops the `.internal.`).
  Example shape from the docs: `myapp.happyhill-70162bb9.canadacentral.azurecontainerapps.io`.
- Reachable only from other apps in the same environment. From outside it resolves, TLS handshake succeeds, and the proxy
  returns **404**. Ingress `allowInsecure=false` (default) enforces https; the short form `http://<app-name>` also works
  inside the environment.
- TLS is terminated by the environment's Envoy proxy; the docs do not say which CA issues the certificate for the default
  domain. Clients inside the environment must therefore trust the platform certificate; **[unverified]** for the .NET apps'
  trust store in practice (expected to be a public CA, but not stated).
- Optional peer-to-peer encryption (private cert, off by default; may raise latency and cut throughput).
- Suitability as Keycloak `iss` / Authority: the internal FQDN is deterministic and stable for the lifetime of the
  environment (it changes if the environment is recreated, because the unique id changes). That makes it usable as an
  issuer **only if** every consumer reaches Keycloak by that same hostname, since Keycloak builds `iss` from its configured
  or request hostname (Keycloak behaviour, not covered by these sources). Consequences: (a) browsers cannot reach it, so the
  Keycloak admin console and any browser redirect flow are unavailable; this repo uses client-credentials between servers,
  so that may be acceptable, (b) the token's `iss` must match what Web, MCP and Agents are configured to expect, which
  `ProviderConfigAgreementTests`-style config agreement should pin. Inference, not a documented guarantee.

## 4. PostgreSQL Flexible Server, Burstable B1ms

| Fact | Value | Source |
|---|---|---|
| Spec | 1 vCore, 2 GiB, max 640 IOPS, 10 MiB/s | [doc] concepts-compute |
| Compute | $0.0199/h = **$14.53/month** | [price] B1MS; [calc] |
| Storage | min **32 GiB** (to 64 TiB); $0.1369 /GB-month, so 32 GiB = **$4.38/month** | [doc] concepts-compute; [price] |
| Backup | retention 7 to 35 days; extra backup storage LRS $0.103 /GB-month (how much is included is not stated in fetched pages) | [doc]; [price] |
| Total floor | **about $18.91/month** | [calc] |
| Max connections | **50, of which 35 usable** (15 reserved); Burstable has **no built-in PgBouncer** | [doc] concepts-limits |
| Production fit | Microsoft calls Burstable "not recommended for production", CPU-credit model, can become unreachable when credits run out | [doc] concepts-compute |
| PostgreSQL 17 | available, Azure standard support 30-Sep-2024 to 8-Nov-2029; PG 18 since 25-Sep-2025 | [doc] concepts-version-policy |
| pgvector | add `vector` (not `pgvector`) to the `azure.extensions` server parameter, then `CREATE EXTENSION vector;` in each database | [doc] how-to-use-pgvector, how-to-allow-extensions |
| Stop/start | a stopped server **auto-starts after 7 days**; management operations unavailable while stopped; monthly maintenance may briefly start it | [doc] concepts-limits; docs.azure.cn how-to-stop-server (Microsoft-operated mirror) |
| Billing while stopped | the fetched pages do not state it; a search summary said compute billing stops and storage continues | **[unverified]** |

The 35-connection ceiling is the sharpest finding: Keycloak plus Web, MCP and Agents (EF Core pools) can exhaust it, so
pool sizes must be capped (or `max_connections` raised with the memory risk the docs warn about).

### Network access options

- Public access: **by default no IP is allowed**; rules are IPv4 only; changes take up to 5 minutes. [doc] concepts-networking-public
- Checkbox "Allow public access from any Azure service within Azure to this server" permits **all Azure traffic including
  other customers' subscriptions**, so authentication is the only gate. [doc]
- Allowing only the Container Apps environment is fragile: its outbound IPs "might change over time", and a stable
  egress IP (NAT Gateway) is supported only in a workload-profiles environment with your own VNet. [doc] networking
- Private access (VNet integration) cannot be combined with public access and cannot be added later ("don't support moving
  in and out of a virtual network"). [doc] concepts-limits. The retail API lists no separate VNet-integration meter for
  Flexible Server (only compute, storage, IOPS, backup); a Container Apps VNet adds a subnet (`/27` minimum). No extra cost
  line was found, but Private Link/private-endpoint per-hour pricing was not queried here.

## 5. Container registry

- ACR Basic: $0.1666/day = **$5.07/month**, includes 10 GiB, extra $0.10 /GB-month; no private link or IP rules (those are
  Premium); 2 webhooks. [price]; [doc] container-registry-skus
- ACR is optional. Container Apps pulls "from any public or private container registry"; a private non-ACR registry takes
  username + password stored as an app secret (`registries[]`, `passwordSecretRef`). Managed-identity pull is ACR-only.
  [doc] containers. The docs warn Docker Hub rate limits make containers fail to start, and recommend ACR for that reason.
- GHCR is not named in the page but is a standard registry that fits this mechanism: a public package needs no credential, a
  private one needs a PAT secret in each app. Azure DevOps Artifacts as a container registry is not covered by these sources
  (**[unverified]**). Dropping ACR saves $5.07/month at the price of a stored credential.

## 6. Monthly totals, side by side [calc]

Topology A: Container Apps (workload-profiles environment, Consumption only) + Postgres Flexible B1ms 32 GiB + ACR Basic.
Topology B: one B2s VM running Docker Compose (Keycloak, three hosts, SPA, Postgres with pgvector in a container); Linux, no registry.

| Line | A idle floor | A active ceiling | A scale-to-zero apps (apps 10% active) | B: B2s VM |
|---|---|---|---|---|
| Compute | $33.86 | $112.86 | $18.09 (Keycloak min 1, apps min 0, 4 apps at 10%) | $31.54 (B2s $0.0432/h) |
| Database | $18.91 | $18.91 | $18.91 | $0 (in the VM) |
| Registry | $5.07 | $5.07 | $5.07 | $0 (build on box or GHCR) |
| Disk | n/a | n/a | n/a | $9.60 (E10 Standard SSD 128 GiB LRS) |
| Public IP | free (platform ingress) | free | free | $3.65 (Standard static) |
| **Total** | **$57.84** | **$136.84** | **$42.07** | **$44.79** |

Add-ons not in the table: Log Analytics (Container Apps default sink; $2.99/GB ingestion after the free 5 GB tier, volume
unknown), egress bandwidth, Premium SSD instead of Standard SSD on the VM (+$12.08, P10 $21.68), Keycloak idle-vs-active
uncertainty, and the unverified $94.90/month environment meter (Topology A would then be $152.74 to $231.74).

Reading the table:

- A is competitive only if the apps are allowed to scale to zero (about $42) or the Keycloak/.NET replicas really stay on
  the idle rate (about $58). Always busy, it is about 3x the VM.
- B2s has 2 vCPU / 4 GiB shared by Keycloak (the Container Apps sizing above gives it 1 GiB), three .NET hosts and Postgres. That is
  tight and B-series is burstable (credit-limited), so a B2ms (8 GiB, $0.0864/h = $63.07) gives a truer comparison: B2ms
  total = $63.07 + 9.60 + 3.65 = **$76.32**, at which point A's idle floor is cheaper than the VM.
- B has no managed Postgres, no IP restrictions primitive beyond an NSG, and no auto-patching; A trades that for the 35
  connection cap and Burstable's "not for production" caveat.

## Open items to verify cheaply

1. Does `ipSecurityRestrictions` on an external app block FQDN calls from sibling apps? (one test environment)
2. Is the $0.13/h `Environment Management Hour` meter charged on a Consumption-only environment? (first invoice or calculator)
3. Does the 240 s ingress limit cut a streaming SSE response that keeps sending data?
4. Billing of a stopped Flexible Server (compute off, storage on?) from the Learn page, not a search summary.
5. Cold-start seconds for Keycloak on a 0.5 vCPU replica.

## Sources

- https://learn.microsoft.com/en-us/azure/container-apps/billing
- https://learn.microsoft.com/en-us/azure/container-apps/environment
- https://learn.microsoft.com/en-us/azure/container-apps/networking
- https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview
- https://learn.microsoft.com/en-us/azure/container-apps/ingress-environment-configuration
- https://learn.microsoft.com/en-us/azure/container-apps/ip-restrictions
- https://learn.microsoft.com/en-us/azure/container-apps/connect-apps
- https://learn.microsoft.com/en-us/azure/container-apps/scale-app
- https://learn.microsoft.com/en-us/azure/container-apps/containers
- https://learn.microsoft.com/en-us/azure/postgresql/compute-storage/concepts-compute
- https://learn.microsoft.com/en-us/azure/postgresql/configure-maintain/concepts-limits
- https://learn.microsoft.com/en-us/azure/postgresql/configure-maintain/concepts-version-policy
- https://learn.microsoft.com/en-us/azure/postgresql/extensions/how-to-use-pgvector
- https://learn.microsoft.com/en-us/azure/postgresql/extensions/how-to-allow-extensions
- https://learn.microsoft.com/en-us/azure/postgresql/network/concepts-networking-public
- https://docs.azure.cn/en-us/postgresql/flexible-server/how-to-stop-server (Microsoft-operated mirror)
- https://learn.microsoft.com/en-us/azure/container-registry/container-registry-skus
- https://azure.microsoft.com/en-us/pricing/details/container-apps/ (dollar cells did not render; used the API)
- https://prices.azure.com/api/retail/prices (serviceName: Azure Container Apps, Azure Database for PostgreSQL,
  Container Registry, Virtual Machines, Storage, Virtual Network, Log Analytics; armRegionName `swedencentral`)
