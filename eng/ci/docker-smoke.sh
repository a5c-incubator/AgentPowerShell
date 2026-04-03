#!/usr/bin/env sh
set -eu

IMAGE_TAG="${1:-agentpowershell:ci}"

docker build -t "$IMAGE_TAG" .
docker run --rm "$IMAGE_TAG" version
docker run --rm "$IMAGE_TAG" policy validate /app/default-policy.yml --output json
docker run --rm --entrypoint /bin/sh "$IMAGE_TAG" -lc '
  /app/AgentPowerShell.Cli start --output json &&
  /app/AgentPowerShell.Cli status --output json &&
  /app/AgentPowerShell.Cli stop --output json
'
