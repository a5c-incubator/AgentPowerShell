# Policy Reference

AgentPowerShell policy documents are YAML files parsed by `PolicyLoader` in `AgentPowerShell.Core`. Rules are evaluated using a first-match-wins strategy.

## Top-Level Fields

- `version`: policy schema version string
- `name`: human-readable policy name
- `description`: free-form description
- `file_rules`: ordered file access rules
- `command_rules`: ordered command rules
- `network_rules`: ordered network rules
- `env_rules`: ordered environment variable rules

## Decisions

Supported decisions used by the repository tests and loaders:

- `allow`
- `deny`
- `approve`

## File Rules

Example:

```yaml
file_rules:
  - name: allow-workspace-read
    pattern: "${WORKSPACE}/**"
    operations: [read, stat]
    decision: allow
```

Fields:

- `name`: stable rule identifier
- `pattern`: glob-style path pattern
- `operations`: one or more of `read`, `write`, `create`, `delete`, `stat`
- `decision`: allow, deny, or approve
- `message`: optional operator-facing explanation

## Command Rules

Example:

```yaml
command_rules:
  - name: deny-dangerous
    pattern: "{rm,shutdown,reboot}"
    decision: deny
```

Command patterns support brace expansion such as `{rm,shutdown,reboot}`.

## Network Rules

Example:

```yaml
network_rules:
  - name: allow-nuget
    domain: "api.nuget.org"
    ports: [443]
    decision: allow
```

Fields:

- `domain`: hostname or wildcard pattern
- `ports`: explicit ports or ranges such as `1-65535`
- `decision`
- `message`

## Environment Rules

Environment rules use variable name patterns and action lists. The current runtime enforces `read` checks for explicit environment overrides that differ from the daemon's own baseline environment before command execution. If `env_rules` are present, unmatched override variables are denied by default.

## Authoring Guidance

- Keep broad allow rules early only when they are intentional.
- Put deny rules above permissive catch-alls.
- Add explicit messages for approvals and denials that operators will see.
- Treat `${WORKSPACE}` and `${HOME}` references as part of your policy contract.
