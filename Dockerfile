# syntax=docker/dockerfile:1

# ═══════════════════════════════════════════════════════════════════════════
#  KnowledgeAssistant API — multi-stage build
#
#  Three stages: restore/build, publish, runtime. The split exists for two
#  reasons that matter more than image size.
#
#  1. The SDK never ships. The final image carries the ASP.NET runtime only —
#     no compiler, no NuGet cache, no source. A shell obtained in a running
#     container finds nothing to rebuild with and no source to read.
#
#  2. Restore is cached on the project files alone. Only *.csproj, the props
#     files, and NuGet.config are copied before `dotnet restore`, so editing a
#     .cs file does not invalidate the restore layer. Copying everything first
#     — the common mistake — re-downloads the entire package graph on every
#     source change.
#
#  Build:  docker build -t knowledge-assistant-api .
#  Run:    see docker-compose.yml, which supplies configuration and credentials.
# ═══════════════════════════════════════════════════════════════════════════


# ─── Stage 1: restore and build ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0-noble AS build
ARG BUILD_CONFIGURATION=Release

# Quiets first-run telemetry and the welcome banner so build logs carry only
# build output. Neither affects the produced binaries.
ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

WORKDIR /src

# Central Package Management means these three files determine the entire
# package graph. They are copied first, with the .csproj files, so the restore
# layer below is keyed on dependency changes rather than on source changes.
#
# NuGet.config matters here beyond convenience: it clears inherited feeds and
# pins package source mapping, so the restore inside this image resolves from
# the same sources a developer's machine does. Omitting it would let the build
# silently use whatever feeds the base image happens to have.
COPY Directory.Build.props Directory.Packages.props NuGet.config ./

COPY KnowledgeAssistant.Domain/*.csproj          KnowledgeAssistant.Domain/
COPY KnowledgeAssistant.Application/*.csproj     KnowledgeAssistant.Application/
COPY KnowledgeAssistant.Infrastructure/*.csproj  KnowledgeAssistant.Infrastructure/
COPY KnowledgeAssistant.Api/*.csproj             KnowledgeAssistant.Api/

# Only the API project graph is restored. The test projects are deliberately
# absent from this image — they are run by CI, not built into a release
# artefact, and pulling xunit and the test SDK in here would add weight to a
# layer that ships nothing.
#
# The packages are written into this layer, not into a BuildKit cache mount. A
# cache mount is not part of the layer cache, and CI exports only the layer
# cache (cache-to: type=gha): a fresh runner would then reuse this layer as
# "already restored" while its package folder is empty, and the --no-restore
# build below fails with NETSDK1064. In the layer, the packages travel with the
# cache hit. This stage is never shipped, so they add nothing to the image.
RUN dotnet restore KnowledgeAssistant.Api/KnowledgeAssistant.Api.csproj

# Source last, so everything above is reused whenever only code has changed.
COPY KnowledgeAssistant.Domain/          KnowledgeAssistant.Domain/
COPY KnowledgeAssistant.Application/     KnowledgeAssistant.Application/
COPY KnowledgeAssistant.Infrastructure/  KnowledgeAssistant.Infrastructure/
COPY KnowledgeAssistant.Api/             KnowledgeAssistant.Api/

# --no-restore because the layer above already did it; without the flag this
# would restore again and the cache above would buy nothing.
RUN dotnet build KnowledgeAssistant.Api/KnowledgeAssistant.Api.csproj \
        --configuration $BUILD_CONFIGURATION \
        --no-restore


# ─── Stage 2: publish ──────────────────────────────────────────────────────
FROM build AS publish
ARG BUILD_CONFIGURATION=Release

# UseAppHost=false drops the native launcher executable. The image starts the
# app with `dotnet KnowledgeAssistant.Api.dll`, so the apphost is dead weight —
# and one fewer platform-specific binary in the artefact.
RUN dotnet publish KnowledgeAssistant.Api/KnowledgeAssistant.Api.csproj \
        --configuration $BUILD_CONFIGURATION \
        --no-restore \
        --output /app/publish \
        -p:UseAppHost=false


# ─── Stage 3: runtime ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble AS final

# OCI labels. The source label is what makes GitHub link a published package to
# this repository, and what lets `docker inspect` on a mystery image in a
# registry answer "where did this come from".
LABEL org.opencontainers.image.title="KnowledgeAssistant API" \
      org.opencontainers.image.description="RAG and agent API over a private document corpus, on Azure AI Foundry and Azure AI Search." \
      org.opencontainers.image.source="https://github.com/MetinTopcu/KnowledgeAssistant" \
      org.opencontainers.image.licenses="Apache-2.0"

ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    ASPNETCORE_ENVIRONMENT=Production \
    # 8080, not 80. A non-root user cannot bind a port below 1024, and this
    # image runs as non-root — see USER below. 8080 is also what the .NET base
    # images have defaulted to since .NET 8.
    ASPNETCORE_URLS=http://+:8080

WORKDIR /app
COPY --from=publish /app/publish .

# The base image ships this unprivileged account. Running as root inside a
# container is not a sandbox: combined with a container escape or a mounted
# volume it is root on something that matters. Nothing here needs privilege —
# the app binds 8080, writes no files, and authenticates with a token from the
# instance metadata endpoint.
USER $APP_UID

EXPOSE 8080

# Liveness rather than readiness, deliberately. This probe decides whether to
# RESTART the container, and readiness fails when Azure Search or Blob is
# unreachable — restarting the app cannot fix somebody else's outage, and doing
# so during a dependency incident turns a degraded service into a crash loop.
# Readiness (/health/ready) belongs in the orchestrator's traffic decision, not
# here. See Observability/HealthEndpointRegistration.cs.
#
# curl and wget are both absent from the runtime image, and adding either just
# to probe would widen the attack surface of every running container for the
# sake of one HTTP GET. Bash's /dev/tcp does the same job with what is already
# installed.
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD ["/usr/bin/timeout", "3", "/bin/bash", "-c", \
         "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/live HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -1 <&3 | grep -q '200 OK'"]

ENTRYPOINT ["dotnet", "KnowledgeAssistant.Api.dll"]
