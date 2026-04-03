#!/usr/bin/env sh
set -eu

RID="${1:-linux-x64}"
CONFIGURATION="${CONFIGURATION:-Release}"
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)
DOTNET_BIN="${DOTNET_HOST_PATH:-}"
WORK_ROOT="$REPO_ROOT/.artifacts/release-smoke/$RID"
PACKAGE_ROOT="$WORK_ROOT/package"

cleanup() {
  rm -rf "$WORK_ROOT"
}

trap cleanup EXIT INT TERM

if [ -z "$DOTNET_BIN" ]; then
  DOTNET_BIN="$(command -v dotnet || true)"
fi
if [ -z "$DOTNET_BIN" ]; then
  DOTNET_BIN="$HOME/.dotnet/dotnet"
fi
if [ ! -x "$DOTNET_BIN" ]; then
  echo "Unable to locate dotnet. Install the .NET 9 SDK or set DOTNET_HOST_PATH." >&2
  exit 1
fi

rm -rf "$WORK_ROOT"
mkdir -p "$PACKAGE_ROOT/daemon"

cd "$REPO_ROOT"
"$DOTNET_BIN" restore agentpowershell.sln -r "$RID"
"$DOTNET_BIN" publish src/AgentPowerShell.Cli/AgentPowerShell.Cli.csproj -c "$CONFIGURATION" -r "$RID" --self-contained true -p:PublishSingleFile=true --no-restore -o "$PACKAGE_ROOT"
"$DOTNET_BIN" publish src/AgentPowerShell.Daemon/AgentPowerShell.Daemon.csproj -c "$CONFIGURATION" -r "$RID" --self-contained true -p:PublishSingleFile=true --no-restore -o "$PACKAGE_ROOT/daemon"

cp default-policy.yml "$PACKAGE_ROOT/default-policy.yml"
cp config.yml "$PACKAGE_ROOT/config.yml"

CLI="$PACKAGE_ROOT/AgentPowerShell.Cli"
DAEMON="$PACKAGE_ROOT/daemon/AgentPowerShell.Daemon"

test -x "$CLI"
test -x "$DAEMON"
test -f "$PACKAGE_ROOT/default-policy.yml"
test -f "$PACKAGE_ROOT/config.yml"

export AGENTPOWERSHELL_DAEMON_PATH="$DAEMON"

"$CLI" version
"$CLI" policy validate "$PACKAGE_ROOT/default-policy.yml" --output json
"$CLI" start --output json
"$CLI" status --output json
"$CLI" stop --output json
