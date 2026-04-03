namespace AgentPowerShell.Events;

public sealed record ReportFinding(string Severity, string Message);

public sealed record SessionReport(
    string SessionId,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<EventRecord> Timeline,
    IReadOnlyList<ReportFinding> Findings,
    IReadOnlyDictionary<string, int> EventCounts);
