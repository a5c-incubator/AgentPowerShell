# Agent Integration

AgentPowerShell is intended to sit in front of automated shell execution and report policy, session, event, and checkpoint state back to the calling tool.

## Example `AGENTS.md`

```md
## AgentPowerShell

- Use `agentpowershell exec <session-id> <command...>` for shell execution.
- Require JSON output for machine parsing.
- Validate `default-policy.yml` before long-running sessions.
- Create a checkpoint before risky file mutations.
```

## Example `CLAUDE.md`

```md
## Shell Gateway

Use AgentPowerShell for commands that should be tracked or policy-checked.

Suggested flow:
1. `agentpowershell session create --output json`
2. `agentpowershell checkpoint create --name before-change --output json`
3. `agentpowershell exec <session-id> <command...> --output json`
```

## Recommended Workflow

1. Create or resume a session.
2. Validate or load the policy.
3. Create a checkpoint for reversible workspace changes.
4. Execute commands through the gateway.
5. Review reports and event output after the task completes.
