#!/usr/bin/env sh
set -eu

CONFIGURATION="${CONFIGURATION:-Release}"
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)
DESTINATION="$(mktemp -d "${TMPDIR:-/tmp}/agentpowershell-install.XXXXXX")"

cleanup() {
  rm -rf "$DESTINATION"
}

trap cleanup EXIT INT TERM

cd "$REPO_ROOT"
CONFIGURATION="$CONFIGURATION" SKIP_PROFILE_UPDATE=true ./install.sh "$DESTINATION"

CLI="$DESTINATION/AgentPowerShell.Cli"
DAEMON="$DESTINATION/daemon/AgentPowerShell.Daemon"

test -x "$CLI"
test -x "$DAEMON"

export AGENTPOWERSHELL_DAEMON_PATH="$DAEMON"

"$CLI" version
"$CLI" policy validate "$DESTINATION/default-policy.yml" --output json
"$CLI" start --output json
"$CLI" status --output json
"$CLI" stop --output json
