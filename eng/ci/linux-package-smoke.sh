#!/usr/bin/env sh
set -eu

RID="${1:-linux-x64}"
SDK_IMAGE="${SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:9.0}"
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)

docker run --rm \
  -v "$REPO_ROOT:/work" \
  -w /work \
  "$SDK_IMAGE" \
  bash -lc "chmod +x install.sh eng/ci/install-smoke.sh eng/ci/release-package-smoke.sh && ./eng/ci/install-smoke.sh && ./eng/ci/release-package-smoke.sh $RID"
