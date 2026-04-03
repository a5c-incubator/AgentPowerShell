using System.Collections.ObjectModel;

namespace AgentPowerShell.Protos;

public sealed record ShimCommandRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string InvocationName { get; init; } = "pwsh";
    public string ExecutablePath { get; init; } = string.Empty;
    public Collection<string> Arguments { get; init; } = [];
    public string WorkingDirectory { get; init; } = System.Environment.CurrentDirectory;
    public bool Interactive { get; init; }
    public Dictionary<string, string> Environment { get; init; } = [];
}

public sealed record ShimCommandResponse
{
    public string SessionId { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public string PolicyDecision { get; init; } = string.Empty;
    public string? DenialReason { get; init; }
    public Collection<ShimEventMessage> Events { get; init; } = [];
}

public sealed record ShimEventMessage(string EventType, string Detail, DateTimeOffset CreatedAt);
