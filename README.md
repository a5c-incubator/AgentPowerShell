# AgentPowerShell

AgentPowerShell is a .NET 9 PowerShell execution gateway for policy-enforced agent workflows. The solution combines a CLI, daemon, shim, event pipeline, approval system, LLM proxy, MCP inspection, and platform-specific enforcement slices.

## Features

- YAML policy loading with first-match-wins evaluation
- Session creation, listing, expiry, and status reporting
- Explicit command execution with JSON/text output and real exit-code propagation
- Hosted PowerShell execution for inline `powershell` and `pwsh` `-Command` invocations
- Event capture, append-only storage, JSON serialization, and report generation
- Approval flows for tty, API, TOTP, and WebAuthn-oriented configurations
- Authentication modes for none, API key, OIDC, and hybrid operation
- LLM proxy routing with redaction and usage tracking
- MCP registry, pinning, and cross-server flow inspection
- Workspace checkpoints with create, list, restore, and dry-run restore preview
- Windows Job Object containment for native child processes
- Windows AppContainer isolation for direct native network-client commands under deny-all network policy

## Quick Start

1. Install the .NET 9 SDK.
2. Clone the repository.
3. Build the solution:

```powershell
dotnet build agentpowershell.sln --verbosity minimal
dotnet test agentpowershell.sln --verbosity minimal --no-build
```

4. Run the CLI:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- version
```

5. Validate the sample policy:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- policy validate default-policy.yml --output json
```

6. Or install the self-contained binaries:

```powershell
./install.ps1
```

## Practical Usage

List sessions:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- session list
```

Run inline PowerShell through the hosted path:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- exec session-a powershell -Command "$ExecutionContext.SessionState.LanguageMode" --output json
```

Run a native command:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- exec session-a dotnet --version --output json
```

Use the built binary directly:

```powershell
.\src\AgentPowerShell.Cli\bin\Debug\net9.0\AgentPowerShell.Cli.exe exec session-a dotnet --version --output json
```

## Blocked Network Example

On Windows, a direct native network client such as `curl.exe` can now be run inside an AppContainer sandbox when the effective policy denies all network access.

Example policy:

```yaml
command_rules:
  - name: allow-all
    pattern: "*"
    decision: allow

network_rules:
  - name: deny-all
    domain: "*"
    ports: ["1-65535"]
    decision: deny
```

Example binary invocation:

```powershell
.\src\AgentPowerShell.Cli\bin\Debug\net9.0\AgentPowerShell.Cli.exe `
  exec blocked-demo `
  "C:\Program Files\Git\mingw64\bin\curl.exe" `
  "https://example.com" `
  --output json
```

Current observed Windows behavior for that case is:

- `policyDecision` remains `allow` because command policy allowed the launch.
- `eventType` shows `process.executed.native.appcontainer`, which tells you the host-level Windows sandbox path was used.
- The native command actually starts.
- The process then fails inside the sandbox, for example with `curl: (6) Could not resolve host: example.com`.

This is intentionally narrower than a full host-wide firewall model. Mixed allowlists still rely on explicit-target policy checks rather than claiming complete OS-level outbound allow/deny parity.

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

- Pull requests and pushes to `main` and `master` run .NET build/test jobs on Windows, Linux, and macOS.
- The CI matrix now also smoke-tests published install outputs, not just repo-local `dotnet` execution paths.
- Docker smoke coverage runs on Linux and Windows GitHub-hosted runners. macOS runners only execute the direct .NET test matrix because hosted macOS runners do not expose a Docker daemon.
- Linux-container smoke coverage now also exercises `install.sh` and the staged release-package layout on Linux and Windows runners.
- Release tags use semantic versioning in the form `vMAJOR.MINOR.PATCH`.
- Tagged releases publish packaged CLI + daemon artifacts and Linux multi-arch Docker images under the `a5c-ai` GitHub organization.
- The nightly workflow publishes `edge` container images to `ghcr.io/a5c-ai/agentpowershell`.

## Status

The repository currently delivers a usable, tested baseline for:

- CLI-driven session management, checkpointing, config updates, reporting, and policy inspection
- `exec` for explicit commands through the shim/daemon processor path
- hosted PowerShell execution for inline `powershell` and `pwsh` `-Command` invocations
- command policy checks, explicit-network prechecks, and Windows Job Object process control
- a narrow Windows host-level network isolation path for native clients under deny-all policy
- env-rule enforcement for explicit environment overrides passed into command execution

The repository does not yet fulfill the full `agentsh`-style specification described in `request.task.md` and `docs/architecture.md`. In particular:

- interactive shell sessions through `exec` are still intentionally unsupported
- Linux and macOS platform enforcers remain mostly structural scaffolding
- broader network enforcement is still mostly policy-aware pre-execution filtering
- the documented long-term architecture is broader than the currently verified runtime behavior

Treat the project as an actively converging implementation rather than a finished parity clone.
