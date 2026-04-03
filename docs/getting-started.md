# Getting Started

## Prerequisites

- .NET 9 SDK
- PowerShell 7+ recommended for local development
- Git for source control and workspace checkpoint workflows

## Build And Test

```powershell
dotnet build agentpowershell.sln --verbosity minimal
dotnet test agentpowershell.sln --verbosity minimal --no-build
```

## First Commands

If you want self-contained binaries instead of `dotnet run`, use:

```powershell
./install.ps1
```

or on native Unix shells:

```bash
./install.sh
```

Print the CLI version:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- version
```

Inspect the sample configuration:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- config show --output json
```

List sessions:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- session list
```

Execute an explicit inline PowerShell command through the current runtime path:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- exec session-a powershell -Command "$ExecutionContext.SessionState.LanguageMode" --output json
```

Start the daemon explicitly when you want the shim or other tools to reuse a long-lived process:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- start --output json
```

Create a workspace checkpoint:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- checkpoint create --name baseline --output json
```

Preview a restore:

```powershell
dotnet run --project src/AgentPowerShell.Cli -- checkpoint restore latest --dry-run --output json
```

## Important Files

- `config.yml`: local service and feature configuration
- `default-policy.yml`: default policy rules for commands, files, and network access
- `.agentpowershell/`: generated runtime state such as sessions and checkpoints

## Development Flow

1. Update policy or config inputs as needed.
2. Run `dotnet build`.
3. Run `dotnet test`.
4. Use JSON CLI output when integrating with other tools or automation.

## Current Boundaries

- `exec` supports explicit commands; it does not yet provide an interactive shell session.
- Inline PowerShell commands route through the hosted execution path that exists today; the broader PSHost/ConstrainedLanguage architecture remains target direction, not a fully verified parity story.
- Native commands use the daemon processor path and current policy prechecks.
- `exec` now returns the underlying command or policy exit code to the calling shell, so denials and runtime failures are observable to automation.
- The shim will attempt to connect to the daemon first and will auto-start it only when a daemon command, binary, or source project can be discovered.
- Cross-platform sandboxing is still uneven; Windows has the most concrete runtime enforcement today.
- Network blocking currently means explicit-target policy filtering, not full OS-level egress interception.
