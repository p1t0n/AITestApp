# The edge image: the built SPA served by nginx, with /api and /agents reverse-proxied to the Web
# and Agents hosts. In the deployed topology this is the only container with public ingress; the
# rest are internal, which is why the browser only ever sees this one origin.
#
#   docker build -f deploy/images/edge.Dockerfile -t etj-edge .
#
# Build context is the repo root, so the nginx template and web/ both come from one place.

FROM node:22-alpine AS spa
WORKDIR /src
# package files first: a dependency-free source change then reuses the install layer.
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
# `npm run build` is tsc --noEmit then vite build, so a type error fails the image, not the deploy.
RUN npm run build

FROM nginx:1.29-alpine

# Rendered at start by the image's own /docker-entrypoint.d/20-envsubst-on-templates.sh. Left to
# itself that step substitutes every environment variable that happens to be defined, and nginx's
# runtime variables ($host, $uri, $scheme) share that namespace — a platform-injected variable
# named like one of them would be swallowed, and the result would still pass nginx -t. The filter
# narrows substitution to exactly the two placeholders this template actually has.
ENV NGINX_ENVSUBST_FILTER='^(WEB_UPSTREAM|AGENTS_UPSTREAM)$' \
    WEB_UPSTREAM=etj-web \
    AGENTS_UPSTREAM=etj-agents

COPY deploy/images/nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=spa /src/dist /usr/share/nginx/html

# Non-root. Three things the master process writes have to belong to the unprivileged user
# first: the rendered config, the proxy cache dirs, and the pid file. The pid file is created
# here rather than its directory chowned — /run holds more than nginx's business, and `chown -R
# /var/run` would not have reached it anyway (on alpine /var/run is a symlink, and chown -R does
# not follow one it is handed).
RUN chown -R nginx:nginx /etc/nginx/conf.d /var/cache/nginx \
 && touch /run/nginx.pid && chown nginx:nginx /run/nginx.pid

USER nginx
EXPOSE 8080
