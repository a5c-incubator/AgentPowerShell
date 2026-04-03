#!/usr/bin/env sh
set -eu

DESTINATION="${1:-$HOME/.local/bin}"
mkdir -p "$DESTINATION"

echo "Publish AgentPowerShell locally before installation:"
echo "dotnet publish src/AgentPowerShell.Cli/AgentPowerShell.Cli.csproj -c Release -p:PublishProfile=linux-x64"
echo "Copy the published files into $DESTINATION."
