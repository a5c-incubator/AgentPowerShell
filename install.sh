#!/usr/bin/env sh
set -eu

DESTINATION="${1:-$HOME/.local/bin}"
CONFIGURATION="${CONFIGURATION:-Release}"
RID="${RID:-}"
SELF_CONTAINED="${SELF_CONTAINED:-true}"
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname "$0")" && pwd)
REPO_ROOT="$SCRIPT_DIR"

if [ -z "$RID" ]; then
  OS="$(uname -s)"
  ARCH="$(uname -m)"
  case "$OS" in
    Linux) OS_PART="linux" ;;
    Darwin) OS_PART="osx" ;;
    *) echo "Unsupported OS: $OS" >&2; exit 1 ;;
  esac
  case "$ARCH" in
    x86_64|amd64) ARCH_PART="x64" ;;
    arm64|aarch64) ARCH_PART="arm64" ;;
    *) echo "Unsupported architecture: $ARCH" >&2; exit 1 ;;
  esac
  RID="$OS_PART-$ARCH_PART"
fi

DOTNET_BIN="${DOTNET_HOST_PATH:-}"
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

mkdir -p "$DESTINATION"
PUBLISH_ROOT="$REPO_ROOT/.artifacts/install/$RID"
CLI_OUT="$PUBLISH_ROOT/cli"
DAEMON_OUT="$PUBLISH_ROOT/daemon"
rm -rf "$PUBLISH_ROOT"

if [ "$SELF_CONTAINED" != "true" ]; then
  echo "Non-self-contained install publishes are not supported by the current solution layout. Use the default self-contained install." >&2
  exit 1
fi

cd "$REPO_ROOT"
"$DOTNET_BIN" publish src/AgentPowerShell.Cli/AgentPowerShell.Cli.csproj -c "$CONFIGURATION" -r "$RID" -p:PublishSingleFile=true --self-contained true -o "$CLI_OUT"
"$DOTNET_BIN" publish src/AgentPowerShell.Daemon/AgentPowerShell.Daemon.csproj -c "$CONFIGURATION" -r "$RID" -p:PublishSingleFile=true --self-contained true -o "$DAEMON_OUT"

cp -R "$CLI_OUT"/. "$DESTINATION"/
cp default-policy.yml "$DESTINATION/default-policy.yml"
cp config.yml "$DESTINATION/config.yml"
mkdir -p "$DESTINATION/daemon"
cp -R "$DAEMON_OUT"/. "$DESTINATION/daemon"/

PROFILE_FILE="${PROFILE_FILE:-$HOME/.profile}"
DAEMON_BINARY="$DESTINATION/daemon/AgentPowerShell.Daemon"
if [ -f "$DAEMON_BINARY" ] && ! grep -q "AGENTPOWERSHELL_DAEMON_PATH=" "$PROFILE_FILE" 2>/dev/null; then
  printf '\nexport AGENTPOWERSHELL_DAEMON_PATH="%s"\n' "$DAEMON_BINARY" >> "$PROFILE_FILE"
fi

echo "Installed AgentPowerShell to $DESTINATION"
echo "RID: $RID"
echo "If needed, reload your shell profile from $PROFILE_FILE"
