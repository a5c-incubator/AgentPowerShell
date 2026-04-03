#!/usr/bin/env sh
set -eu

IMAGE_TAG="${1:-agentpowershell:ci}"

docker build -t "$IMAGE_TAG" .
docker run --rm "$IMAGE_TAG" version
docker run --rm "$IMAGE_TAG" policy validate /app/default-policy.yml --output json
