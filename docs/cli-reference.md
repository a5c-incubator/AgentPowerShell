# CLI Reference

The current CLI surface is implemented in `src/AgentPowerShell.Cli/CliApp.cs`.

## Global Options

- `--output text|json`
- `--events summary|detailed`

## Commands

### `version`

Print the CLI version string.

### `start`

Start the local daemon process and persist its PID under `.agentpowershell/daemon.json`.
Resolution order:
- `AGENTPOWERSHELL_DAEMON_CMD`
- `AGENTPOWERSHELL_DAEMON_PATH`
- sibling `AgentPowerShell.Daemon(.exe|.dll)` next to the current binaries
- `src/AgentPowerShell.Daemon/AgentPowerShell.Daemon.csproj` from the repo root

If no launch target is discoverable, the command returns `status: unavailable` with guidance.

### `stop`

Stop the tracked daemon PID from `.agentpowershell/daemon.json` and remove that state file. If the process is already gone, the command still cleans up the stale state.

### `exec <session-id> <command...>`

Execute an explicit command in a session through the current daemon processor path. Inline PowerShell commands route through the hosted constrained runspace when supported. Interactive shell launches such as bare `powershell` or `pwsh` are rejected by design.

### `session create`

Create a new session identifier.

### `session list`

List persisted sessions from `.agentpowershell/sessions.json`. Text mode includes the active flag, working directory, created/last/expires timestamps, and policy path. JSON mode emits a stable summary shape.

### `session destroy <session-id>`

Remove a session from the session store.

### `policy validate <path>`

Load and validate a policy file.

### `policy show <path>`

Load and emit the parsed policy document.

### `report [--session-id <id>]`

Generate a markdown session report from the configured JSONL event store and write it under `.agentpowershell/reports`.

### `status`

Show daemon/session status summary, including daemon PID, start time, process name, and tracked working directory when available.

### `checkpoint create [--name <name>]`

Create a workspace checkpoint under `.agentpowershell/checkpoints`.

### `checkpoint list`

List available workspace checkpoints.

### `checkpoint restore [checkpoint-id] [--dry-run]`

Restore a checkpoint or preview the changes without modifying the workspace. If no checkpoint id is supplied, `latest` is used.

### `config show`

Read `config.yml` if present and emit the parsed config model.

### `config set <key> <value>`

Persist supported scalar config keys back to `config.yml`. Unsupported keys return `updated: false`.
