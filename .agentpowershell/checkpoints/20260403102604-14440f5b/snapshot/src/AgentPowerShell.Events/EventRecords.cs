namespace AgentPowerShell.Events;

public abstract record EventRecord(string EventType, DateTimeOffset Timestamp, string SessionId);

public sealed record FileEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    string Path,
    string Operation,
    string Decision) : EventRecord("file", Timestamp, SessionId);

public sealed record ProcessEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    string CommandLine,
    int ExitCode) : EventRecord("process", Timestamp, SessionId);

public sealed record NetworkEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    string Destination,
    int Port,
    string Protocol) : EventRecord("network", Timestamp, SessionId);

public sealed record DnsEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    string Query,
    string Result) : EventRecord("dns", Timestamp, SessionId);

public sealed record ApprovalEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    string Mode,
    bool Approved,
    string Reason) : EventRecord("approval", Timestamp, SessionId);

public sealed record PtyEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    string TerminalId,
    string Mode) : EventRecord("pty", Timestamp, SessionId);

public sealed record LlmEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    string Provider,
    string Model,
    int TokenCount) : EventRecord("llm", Timestamp, SessionId);
