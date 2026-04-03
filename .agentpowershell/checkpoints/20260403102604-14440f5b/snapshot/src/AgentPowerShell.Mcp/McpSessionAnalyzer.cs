namespace AgentPowerShell.Mcp;

public sealed class McpSessionAnalyzer
{
    private readonly List<McpToolCall> _history = [];

    public McpInspectionResult Analyze(McpToolCall call)
    {
        var windowStart = call.Timestamp.AddSeconds(-30);
        var relatedRead = _history.LastOrDefault(previous =>
            previous.Timestamp >= windowStart
            && !string.Equals(previous.ServerId, call.ServerId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(previous.Category, "read", StringComparison.OrdinalIgnoreCase));

        _history.Add(call);

        if (relatedRead is not null && string.Equals(call.Category, "send", StringComparison.OrdinalIgnoreCase))
        {
            return new McpInspectionResult(false, $"Cross-server exfiltration detected from {relatedRead.ServerId} to {call.ServerId}.", "cross_server_flow");
        }

        return new McpInspectionResult(true, "Allowed.");
    }
}
