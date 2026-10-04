# The MCP server: the only path from a model to the roster. Internal, and not reachable from
# outside the environment at all — the Agents host holds a client-credentials token to call it.
#
#   docker build -f deploy/images/mcp.Dockerfile -t etj-mcp .
#
# Build context is the repo root: global.json and Directory.Packages.props live there, and this
# host reaches back into api/Application and api/Infrastructure. The four .NET images are four
# files rather than one parameterised file so that a build command needs no arguments; what keeps
# them from drifting is deploy/images/dockerfiles.test.sh, which asserts the shared contract.

# `11.0` is the RC channel tag, and today it carries exactly the SDK global.json pins.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY global.json Directory.Packages.props ./
COPY api/ ./api/
RUN dotnet publish api/Mcp/ExpertToJob.Mcp.csproj --configuration Release --output /app

FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app
COPY --from=build /app .
# One port, every host: Container Apps targets a single port per app and the nginx edge's
# upstreams assume this one. Production is the effective environment — nothing in the image
# overrides it, and appsettings.Development.json never reaches the build context (.dockerignore).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# `app` (uid 1654) ships in the runtime image; an image that never names a USER runs as root.
USER app
ENTRYPOINT ["dotnet", "ExpertToJob.Mcp.dll"]
