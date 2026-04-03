namespace AgentPowerShell.Mcp;

public sealed record McpToolEntry(string ToolName, string ServerId, string Version, string Hash);

public sealed record McpInspectionResult(bool Allowed, string Reason, string? Rule = null);

public sealed record McpToolCall(string SessionId, string ServerId, string ToolName, string Category, DateTimeOffset Timestamp);
