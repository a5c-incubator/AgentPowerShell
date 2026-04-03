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
dotnet run --project src/AgentPowerShell.Cli -- session list --output json
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
