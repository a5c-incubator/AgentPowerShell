# AgentPowerShell Solution Structure

> This document describes the repository as it exists today. It is intentionally narrower than older drafts that mixed implemented code with aspirational type inventories.

## Solution Layout

```text
agentpowershell.sln
src/
  AgentPowerShell.Core/
  AgentPowerShell.Cli/
  AgentPowerShell.Daemon/
  AgentPowerShell.Shim/
  AgentPowerShell.Events/
  AgentPowerShell.LlmProxy/
  AgentPowerShell.Mcp/
  AgentPowerShell.Platform.Windows/
  AgentPowerShell.Platform.Linux/
  AgentPowerShell.Platform.MacOS/
  AgentPowerShell.Protos/
tests/
  AgentPowerShell.Tests/
  AgentPowerShell.IntegrationTests/
  AgentPowerShell.Platform.Tests/
```

## Project Roles

| Project | Current role |
|---|---|
| `AgentPowerShell.Core` | Shared policy/config/session/checkpoint models plus the first-match-wins evaluation engine and daemon launch resolution helpers |
| `AgentPowerShell.Cli` | Current command-line surface for `exec`, session lifecycle, status, network checks, policy inspection, reports, checkpoints, and config updates |
| `AgentPowerShell.Daemon` | Background worker, shim IPC path, session store, event sink wiring, approval/authentication helpers, and command processing |
| `AgentPowerShell.Shim` | PATH-based PowerShell shim that resolves the real shell, forwards commands to the daemon, and attempts daemon autostart |
| `AgentPowerShell.Events` | Append-only event store, event bus, JSON serialization, and session report generation |
| `AgentPowerShell.LlmProxy` | LLM proxy models, routing helpers, redaction, and telemetry support |
| `AgentPowerShell.Mcp` | MCP registry, inspection, version pinning, and session analysis helpers |
| `AgentPowerShell.Platform.Windows` | Windows-specific enforcement planning plus the repo's most concrete platform runtime slice via Job Object support |
| `AgentPowerShell.Platform.Linux` | Linux-specific enforcement plan generation and native method stubs; not a runtime-complete sandbox implementation |
| `AgentPowerShell.Platform.MacOS` | macOS-specific enforcement plan generation and native method stubs; not a runtime-complete sandbox implementation |
| `AgentPowerShell.Protos` | Shared protocol definitions and shim message models |

## Test Projects

| Project | Current scope |
|---|---|
| `AgentPowerShell.Tests` | Unit and behavior coverage for policy evaluation, CLI behavior, approvals, reports, checkpoints, network checks, and command-processing paths |
| `AgentPowerShell.IntegrationTests` | Smoke-style end-to-end checks for CLI/runtime flows |
| `AgentPowerShell.Platform.Tests` | Cross-platform buildable smoke coverage, not deep native-enforcement validation |

## Current Build And Packaging Surface

The repository currently supports:

- `dotnet build agentpowershell.sln`
- `dotnet test agentpowershell.sln`
- self-contained publish profiles for the CLI project
- install scripts in [install.ps1](/C:/work/agentpowershell/install.ps1) and [install.sh](/C:/work/agentpowershell/install.sh)
- Docker packaging through [Dockerfile](/C:/work/agentpowershell/Dockerfile)
- CI validation in [.github/workflows/ci.yml](/C:/work/agentpowershell/.github/workflows/ci.yml)
- release packaging in [.github/workflows/release.yml](/C:/work/agentpowershell/.github/workflows/release.yml)

Recent verification added:

- install-smoke coverage from published outputs
- Docker smoke coverage that exercises `version`, `policy validate`, `start`, `status`, and `stop`
- release staging that packages CLI, daemon, `default-policy.yml`, and `config.yml`

## Important Reality Checks

This repository does **not** currently justify claims such as:

- completed `agentsh`-style shell replacement
- interactive `exec` parity
- runtime-complete Linux or macOS sandbox enforcement
- OS-level network interception/blocking
- exhaustive current-type inventories matching older architecture drafts

If you need the most reliable source of truth for what actually exists, prefer:

1. the source files under `src/`
2. the tests under `tests/`
3. the CLI surface in [CliApp.cs](/C:/work/agentpowershell/src/AgentPowerShell.Cli/CliApp.cs)
4. the smoke scripts under `eng/ci/`

## Practical Reading Guide

- Use [README.md](/C:/work/agentpowershell/README.md) for project status and top-level usage.
- Use [docs/getting-started.md](/C:/work/agentpowershell/docs/getting-started.md) for local commands.
- Use [docs/cross-platform.md](/C:/work/agentpowershell/docs/cross-platform.md) for platform maturity notes.
- Use [docs/architecture.md](/C:/work/agentpowershell/docs/architecture.md) for the verified runtime model plus roadmap direction.
