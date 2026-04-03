# Contributing

## Development

1. Build the solution.
2. Run the full test suite.
3. Keep changes aligned with the existing project split across `Core`, `Daemon`, `Cli`, `Events`, `LlmProxy`, `Mcp`, and platform-specific projects.

## Coding Standards

- Target .NET 9
- Treat warnings as errors
- Keep nullable annotations enabled
- Add or update tests with behavioral changes

## Pull Requests

- Summarize the user-visible effect of the change.
- Call out policy, eventing, packaging, or security impacts.
- Include test evidence from `dotnet build` and `dotnet test`.
