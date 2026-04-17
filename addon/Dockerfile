# syntax=docker/dockerfile:1.7

ARG BUILD_FROM=ghcr.io/home-assistant/base:3.23

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS dotnet-build

ARG TARGETARCH
ARG BUILD_VERSION=0.1.0

WORKDIR /src

COPY WorkflowServer.sln ./
COPY src/Workflow.Api/Workflow.Api.csproj ./src/Workflow.Api/
COPY src/Workflow.Web/Workflow.Web.csproj ./src/Workflow.Web/
COPY src/Workflow.Engine/Workflow.Engine.csproj ./src/Workflow.Engine/
COPY src/Workflow.Persistence/Workflow.Persistence.csproj ./src/Workflow.Persistence/

RUN dotnet restore ./WorkflowServer.sln

COPY src ./src

RUN \
    case "${TARGETARCH}" in \
      amd64) dotnet_rid="linux-musl-x64" ;; \
      arm64) dotnet_rid="linux-musl-arm64" ;; \
      *) echo "Unsupported TARGETARCH: ${TARGETARCH}" && exit 1 ;; \
    esac \
    && dotnet publish ./src/Workflow.Api/Workflow.Api.csproj \
      --configuration Release \
      --runtime "${dotnet_rid}" \
      --self-contained true \
      --output /app/api \
      -p:Version="${BUILD_VERSION}" \
      -p:PublishSingleFile=true \
      -p:PublishTrimmed=false \
      -p:InvariantGlobalization=true \
    && dotnet publish ./src/Workflow.Web/Workflow.Web.csproj \
      --configuration Release \
      --runtime "${dotnet_rid}" \
      --self-contained true \
      --output /app/web \
      -p:Version="${BUILD_VERSION}" \
      -p:PublishSingleFile=true \
      -p:PublishTrimmed=false \
      -p:InvariantGlobalization=true \
      -p:SkipWorkflowWebFrontendBuild=true

FROM ${BUILD_FROM}

ARG BUILD_VERSION=0.1.0

WORKDIR /opt/workflow

COPY --from=dotnet-build /app/api ./api
COPY --from=dotnet-build /app/web ./web
COPY addon/run.sh /run.sh

RUN chmod a+x /run.sh

LABEL \
    io.hass.version="${BUILD_VERSION}" \
    io.hass.type="addon" \
    org.opencontainers.image.title="Workflow Server" \
    org.opencontainers.image.description="Workflow builder and deterministic runtime for Home Assistant ingress." \
    org.opencontainers.image.source="https://github.com/MaxMinsk/HAWorkflows"

CMD ["/run.sh"]
