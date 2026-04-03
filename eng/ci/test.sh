#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)
cd "$REPO_ROOT"

dotnet build agentpowershell.sln --verbosity minimal
dotnet test agentpowershell.sln --verbosity minimal --no-build
