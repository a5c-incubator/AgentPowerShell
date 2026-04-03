# AgentPowerShell

AgentPowerShell is a .NET 9 PowerShell execution gateway for policy-enforced agent workflows. The solution combines a CLI, daemon, shim, event pipeline, approval system, LLM proxy, MCP inspection, and cross-platform enforcement abstractions.

## Features

- YAML policy loading with first-match-wins evaluation
- Session creation, listing, expiry, and status reporting
- Event capture, append-only storage, JSON serialization, and report generation
- Approval flows for tty, API, TOTP, and WebAuthn-oriented configurations
- Authentication modes for none, API key, OIDC, and hybrid operation
- LLM proxy routing with redaction and usage tracking
- MCP registry, pinning, and cross-server flow inspection
- Workspace checkpoints with create, list, restore, and dry-run restore preview

## Quick Start

1. Install the .NET 9 SDK.
2. Clone the repository.
3. Build the solution:

```powershell
dotnet build agentpowershell.sln
```

4. Run the CLI:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- version
```

5. Validate the sample policy:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- policy validate default-policy.yml --output json
```

## Repository Layout

- `src/AgentPowerShell.Core`: policy, config, and shared models
- `src/AgentPowerShell.Daemon`: daemon services such as sessions and authentication
- `src/AgentPowerShell.Cli`: command-line entrypoint
- `src/AgentPowerShell.Events`: event records, stores, and reporting
- `src/AgentPowerShell.LlmProxy`: provider routing, redaction, and telemetry
- `src/AgentPowerShell.Mcp`: MCP inventory, pinning, and inspection
- `src/AgentPowerShell.Platform.*`: platform-specific enforcement building blocks
- `tests/*`: unit, integration, and platform tests

## Documentation

- `docs/getting-started.md`
- `docs/policy-reference.md`
- `docs/cli-reference.md`
- `docs/configuration.md`
- `docs/cross-platform.md`
- `docs/agent-integration.md`

## Release And CI

- Pull requests and pushes to `main` and `master` run the cross-platform CI workflow.
- Release tags use semantic versioning in the form `vMAJOR.MINOR.PATCH`.
- Tagged releases publish CLI artifacts and Docker images under the `a5c-ai` GitHub organization.
- The nightly workflow publishes `edge` container images to `ghcr.io/a5c-ai/agentpowershell`.

## Status

The repository currently delivers a usable, tested baseline for:

- CLI-driven session management, checkpointing, config updates, reporting, and policy inspection
- `exec` for explicit commands through the shim/daemon processor path
- hosted PowerShell execution for inline `powershell` and `pwsh` `-Command` invocations
- command policy checks, explicit-network prechecks, and first-pass Windows Job Object process control

The repository does not yet fulfill the full `agentsh`-style specification described in `request.task.md` and `docs/architecture.md`. In particular:

- interactive shell sessions through `exec` are still intentionally unsupported
- Linux and macOS platform enforcers remain mostly structural scaffolding
- network blocking is policy-aware pre-execution filtering, not full OS-level egress interception
- the documented long-term architecture is broader than the currently verified runtime behavior

Treat the project as an actively converging implementation rather than a finished parity clone.
