# The production Keycloak image (EXP-117): the official distribution plus one file.
#
# 26.0 matches the tag the AppHost runs locally (api/AppHost/Program.cs) — the realm this imports
# is generated from the one the AppHost imports, so a drift between the two tags would mean the
# deployed authorization server behaves differently from the one every test ran against.
FROM quay.io/keycloak/keycloak:26.0

# The only thing this image adds. --chown because the base image runs as uid 1000, which has to be
# able to read what the startup import reads. `realm-export.prod.json` is generated from
# `realm-export.json` by `dotnet run --project tools/MakeProdRealm` and is checked in; it carries
# no secret — every client secret in it is a `${AGENT_*_SECRET}` placeholder Keycloak resolves
# from the environment at import time.
COPY --chown=1000:0 realm-export.prod.json /opt/keycloak/data/import/realm-export.prod.json

# Nothing about *where* this runs is baked in. Hostname (KC_HOSTNAME), database (KC_DB,
# KC_DB_URL, KC_DB_USERNAME, KC_DB_PASSWORD), the admin bootstrap (KC_BOOTSTRAP_ADMIN_*) and the
# eight AGENT_*_SECRET values all arrive as environment variables at run time, so this image is
# the same artifact in every environment and holds no secret.
#
# --http-enabled: TLS is terminated at the edge; this listens on plain HTTP inside the network.
# --proxy-headers=xforwarded: without it Keycloak builds issuer/redirect URLs from the internal
#   address and every token it mints names the wrong issuer.
# --health-enabled: /health/ready on the management port (9000), which is what a container
#   platform probes. It never appears on 8080.
#
# No `kc.sh build` here on purpose: KC_DB is a build-time option, so baking it would nail the
# database vendor into the image and contradict "configuration comes from the environment".
# `start` re-augments on boot instead, which costs a few seconds of startup.
#
# ENTRYPOINT is the base image's /opt/keycloak/bin/kc.sh, so these are its arguments.
CMD ["start", "--import-realm", "--http-enabled=true", "--proxy-headers=xforwarded", "--health-enabled=true"]
