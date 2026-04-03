FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY . .
RUN case "$TARGETARCH" in \
      "amd64") RID="linux-x64" ;; \
      "arm64") RID="linux-arm64" ;; \
      *) echo "Unsupported TARGETARCH: $TARGETARCH" && exit 1 ;; \
    esac \
    && dotnet publish src/AgentPowerShell.Cli/AgentPowerShell.Cli.csproj -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true -o /out/cli

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0
WORKDIR /app
COPY --from=build /out/cli .
COPY default-policy.yml /app/default-policy.yml
COPY config.yml /app/config.yml

ENTRYPOINT ["./AgentPowerShell.Cli"]
