# Container images

Five images, all built from the repo root, all defined under `deploy/images/`. They are the unit
the Azure deployment ships (EXP-115); nothing here pushes to a registry or touches Azure — tagging
and pushing belong to the deploy workflow, and the Container Apps that run them to the Bicep.

| Image | Dockerfile | What it is | Ingress |
|---|---|---|---|
| `etj-web` | `web.Dockerfile` | the Web API — `/api/*`, the passkey ceremonies, the host that issues the session JWT | internal |
| `etj-mcp` | `mcp.Dockerfile` | the MCP server, the only path from a model to the roster | internal |
| `etj-agents` | `agents.Dockerfile` | the Agents host — `/agents/*`, including the one endpoint that streams | internal |
| `etj-migrator` | `migrator.Dockerfile` | one-shot: EF migrations + base seed, then exits | none (a job) |
| `etj-edge` | `edge.Dockerfile` | the built SPA on nginx, reverse-proxying `/api` and `/agents` | public |

```bash
docker build -f deploy/images/web.Dockerfile -t etj-web .      # and mcp / agents / migrator / edge
```

The context is the repo root for all five: `global.json` and `Directory.Packages.props` live there,
every host reaches back into `api/Application` and `api/Infrastructure`, and the edge needs both
`web/` and the nginx template. `.dockerignore` is what keeps that context from being the whole
working tree — and what guarantees a local `appsettings.Development.json` cannot reach a layer.

## What the four .NET images agree on

They are four files rather than one parameterised file, so a build command needs no arguments.
What stops them drifting is `deploy/images/dockerfiles.test.sh`, which asserts the shared contract:
the SDK image matches `global.json`'s pin, the runtime is `aspnet:11.0`, `USER app` is named out
loud, `ASPNETCORE_HTTP_PORTS=8080`, `ASPNETCORE_ENVIRONMENT=Production`, and the entry assembly is
the one the Dockerfile's project actually produces. Add a fifth .NET host and that test is the
place that learns about it.

Port 8080 is not a preference: Container Apps targets one port per app, and the edge's upstreams
assume it.

## The edge

`nginx/default.conf.template` is rendered into `/etc/nginx/conf.d/default.conf` at container start
by the nginx image's own envsubst step. Two variables are substituted and no others —
`WEB_UPSTREAM` (default `etj-web`) and `AGENTS_UPSTREAM` (default `etj-agents`), narrowed by
`NGINX_ENVSUBST_FILTER` so that nginx's own `$host` / `$uri` / `$scheme`, which share a namespace
with the container's environment, cannot be eaten by a variable the platform injects later.

Three things about the routing are load-bearing:

* **The prefixes pass through.** `/api/` goes to `http://$WEB_UPSTREAM/api/` and `/agents/` to
  `http://$AGENTS_UPSTREAM/agents/`. The Web controllers are routed at `api/...`, the Agents
  endpoints at `/agents/...`, and the Vite dev proxy rewrites nothing — so the SPA's two axios
  base URLs mean the same thing in dev and in the deployment.
* **`/agents/` does not buffer.** `POST /agents/staffing` is Server-Sent Events, and a run can
  take minutes. Buffering off, caching off, `proxy_read_timeout 3600s`, an empty `Connection`
  header, and `X-Accel-Buffering: no` on the way out for the ingress in front of this edge.
* **Everything else is `index.html`.** react-router owns those paths, so an unknown one boots the
  app rather than 404ing. This is unconditional, `/assets/` included: a stale client asking for a
  hashed bundle that no longer exists gets HTML, not a 404. `smoke.sh` asserts that, so narrowing
  it later is a decision someone makes rather than a thing that quietly changes.

nginx resolves each upstream host once, when it loads the config — so the edge will not start
while a named upstream does not resolve. In the deployment the internal FQDNs exist as soon as the
apps do, which is Bicep's ordering problem; locally it just means starting upstreams first, which
is what `smoke.sh` does.

Both nginx and the four hosts run unprivileged. For nginx that takes three things owned by the
`nginx` user before `USER` switches: the rendered config, the cache dirs, and the pid file. The pid
file is created and chowned individually — on alpine `/var/run` is a symlink and `chown -R` does
not follow one it is handed, which is a silent `Permission denied` at start if you assume it does.

## Checking them

```bash
./deploy/images/dockerfiles.test.sh    # the shared contract; no Docker needed
./deploy/images/nginx-conf.test.sh     # renders the template, then nginx -t on the result
./deploy/images/smoke.sh               # builds all five, runs the edge against stub upstreams
```

CI runs the first two in the `Images (build)` job, after building all five. `smoke.sh` is local:
it stands the edge up in front of two stub upstreams and asks what the config cannot answer — that
`/` serves the built SPA, that `/api/health` reaches the Web upstream with its prefix intact, and
that a stream reaches the client in pieces rather than in one lump at the end.

That last one is worth being precise about. It is not a test of `proxy_buffering off`: measured on
2026-10-03 against nginx 1.29-alpine, flipping that directive back on does not make the timing
fail, because nginx forwards small chunks promptly either way when the client reads them. The
directive is pinned as a directive by `nginx-conf.test.sh`. What the timing catches is the path
going quiet end to end — a later `gzip on`, a buffering default that moves, an upstream that stops
flushing — which reading the config will never show you.

Behind a proxy, `npm ci` and `dotnet restore` run inside the build container with none of your
shell's environment. `smoke.sh` forwards `HTTP_PROXY` / `HTTPS_PROXY` / `NO_PROXY` as build args
when they are set; a direct `docker build` needs `--build-arg` for each.
