namespace AgentPowerShell.Core;

public abstract record AgentEvent(string EventType, DateTimeOffset Timestamp, string SessionId);

public sealed record FileEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    string Path,
    string Operation,
    PolicyDecision Decision) : AgentEvent("file", Timestamp, SessionId);

public sealed record ProcessEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    string CommandLine,
    int? ExitCode) : AgentEvent("process", Timestamp, SessionId);

public sealed record NetworkEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    string Domain,
    int Port,
    PolicyDecision Decision) : AgentEvent("network", Timestamp, SessionId);

public sealed record ApprovalEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    string Reason,
    bool Approved) : AgentEvent("approval", Timestamp, SessionId);
