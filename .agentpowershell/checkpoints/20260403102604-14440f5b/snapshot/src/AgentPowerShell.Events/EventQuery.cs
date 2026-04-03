namespace AgentPowerShell.Events;

public sealed record EventQuery(
    string? EventType = null,
    string? SessionId = null,
    DateTimeOffset? Since = null);

public static class EventFilter
{
    public static IReadOnlyList<EventRecord> Apply(IEnumerable<EventRecord> events, EventQuery query) =>
        events.Where(record =>
                (query.EventType is null || string.Equals(record.EventType, query.EventType, StringComparison.OrdinalIgnoreCase)) &&
                (query.SessionId is null || string.Equals(record.SessionId, query.SessionId, StringComparison.OrdinalIgnoreCase)) &&
                (query.Since is null || record.Timestamp >= query.Since))
            .ToArray();
}
