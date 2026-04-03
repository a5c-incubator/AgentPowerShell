FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish src/AgentPowerShell.Cli/AgentPowerShell.Cli.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o /out/cli

FROM mcr.microsoft.com/dotnet/runtime-deps:9.0
WORKDIR /app
COPY --from=build /out/cli .

ENTRYPOINT ["./AgentPowerShell.Cli"]
