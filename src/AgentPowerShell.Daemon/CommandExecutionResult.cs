namespace AgentPowerShell.Daemon;

internal sealed record CommandExecutionResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    string EventType,
    string EventDetail);
