# CLI Reference

The current CLI surface is implemented in `src/AgentPowerShell.Cli/CliApp.cs`.

## Global Options

- `--output text|json`
- `--events summary|detailed`

## Commands

### `version`

Print the CLI version string.

### `start`

Emit daemon start intent.

### `stop`

Emit daemon stop intent.

### `exec <session-id> <command...>`

Emit a request to execute a command in a session.

### `session create`

Create a new session identifier.

### `session list`

List persisted sessions from `.agentpowershell/sessions.json`.

### `session destroy <session-id>`

Remove a session from the session store.

### `policy validate <path>`

Load and validate a policy file.

### `policy show <path>`

Load and emit the parsed policy document.

### `report [--session-id <id>]`

Emit report generation metadata.

### `status`

Show daemon/session status summary.

### `checkpoint create [--name <name>]`

Create a workspace checkpoint under `.agentpowershell/checkpoints`.

### `checkpoint list`

List available workspace checkpoints.

### `checkpoint restore [checkpoint-id] [--dry-run]`

Restore a checkpoint or preview the changes without modifying the workspace. If no checkpoint id is supplied, `latest` is used.

### `config show`

Read `config.yml` if present and emit the parsed config model.

### `config set <key> <value>`

Emit a config update intent. The current implementation reports the requested change and leaves persistence to future work.
