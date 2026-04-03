# AgentPowerShell Architecture

> This document is a current-state architecture reference. It separates verified runtime behavior from longer-term design direction so the repository does not present target-state plans as completed features.

## Verified Runtime Today

AgentPowerShell is currently a .NET 9 PowerShell execution gateway centered on a CLI, a long-lived daemon, a shim, a policy engine, and append-only event/reporting support.

The repository has verified behavior for:

- CLI-driven session creation, listing, destruction, status, reports, checkpoints, config updates, and policy inspection
- explicit command execution through the shim-to-daemon processor path
- inline `powershell` and `pwsh` `-Command` execution through the hosted execution path
- policy-backed command checks, explicit network-target checks, and environment override filtering
- first-pass Windows Job Object process control for native child processes
- self-contained install and Docker packaging flows that now have real smoke coverage

The repository does **not** yet provide a completed `agentsh`-style drop-in shell replacement, interactive `exec` sessions, OS-level outbound network interception, or runtime-complete Linux/macOS sandboxing.

## Runtime Flow

The currently verified flow is:

1. A caller runs the CLI directly or invokes the shim.
2. The shim resolves the real PowerShell path and tries to send a `ShimCommandRequest` to the daemon.
3. If the daemon is unavailable, the shim attempts a local daemon autostart and retries.
4. The daemon evaluates command, environment, and explicit network intent against the configured policy.
5. The daemon executes through the hosted PowerShell path or native process launcher, emits events, and returns structured output.

## Main Components

### CLI

`src/AgentPowerShell.Cli` is the user-facing entry point. It owns:

- `exec`, `start`, `stop`, `status`
- `session create/list/destroy`
- `policy validate/show`
- `network check`
- `report`
- `checkpoint create/list/restore`
- `config show/set`

The CLI is the most reliable source of truth for the currently implemented command surface.

### Daemon

`src/AgentPowerShell.Daemon` hosts the background worker and the command-processing path. The current daemon implementation wires together:

- session persistence
- append-only event storage
- approval helpers
- authentication helpers
- explicit network and filesystem monitoring helpers
- shim IPC handling

The daemon is real and usable, but it is materially smaller than the original target architecture described in earlier drafts of this repository.

### Shim

`src/AgentPowerShell.Shim` is a PowerShell-oriented shim that resolves the real shell, forwards requests to the daemon, and can trigger daemon autostart. It is a PATH-based interception approach, not a finished `/bin/sh`-style shell replacement.

### Policy Engine

`src/AgentPowerShell.Core` contains the policy and config models plus the first-match-wins policy engine. Current verified evaluation covers:

- command rules
- file rules
- network rules for explicit targets
- environment allow/deny handling for explicit overrides

Policy evaluation exists today, but some richer architecture-doc concepts from earlier drafts are still target direction rather than verified runtime behavior.

### Events And Reports

`src/AgentPowerShell.Events` provides append-only event persistence, JSON serialization, and session report generation. This is part of the current runtime, not only a design stub.

### Platform Projects

The platform projects do not have parity with the original target vision:

- Windows: has the most concrete runtime slice today, centered on Job Object based process control
- Linux: currently builds policy-derived enforcement plans but does not provide runtime-complete cgroups/Landlock/seccomp/eBPF enforcement
- macOS: currently builds policy-derived enforcement plans but does not provide runtime-complete Endpoint Security / Network Extension / sandbox execution enforcement

Use `docs/cross-platform.md` as the concise support note for platform maturity.

## Packaging And Verification

The repository now has verified packaging behavior for:

- self-contained install scripts on the supported native shells
- Docker image publishing of both CLI and daemon
- Docker smoke coverage for Linux containers on Linux and Windows runners
- direct .NET build/test coverage on Windows, Linux, and macOS

Release packaging is closer to reality than before, but native Linux/macOS install execution and real tagged-release execution still need runner-backed confirmation outside this local Windows session.

## Current Boundaries

Do not treat the repository as having finished parity with `agentsh`. The biggest current boundaries are:

- `exec` is explicit-command oriented and intentionally non-interactive
- the shim is not yet a polished drop-in shell replacement experience
- network blocking is policy-aware pre-execution filtering, not OS-level egress interception
- Windows enforcement is partial and Linux/macOS enforcement is mostly structural
- architecture-level claims about PSHost, ConstrainedLanguage, driver-backed file interception, and deep platform-native controls are not all backed by the current runtime

## Target Direction

The longer-term direction is still useful, but it should be read as roadmap rather than shipped behavior:

- deeper PowerShell host control and constrained-runspace enforcement
- richer shim installation and shell interception
- stronger platform-native filesystem and network enforcement
- more complete release/install verification across all supported operating systems

When there is a conflict between roadmap wording and observed behavior, trust the source tree, tests, CLI surface, and smoke scripts over earlier architectural intent.
