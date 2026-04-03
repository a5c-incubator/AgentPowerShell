namespace AgentPowerShell.Mcp;

public sealed class McpRegistry
{
    private readonly Dictionary<string, McpToolEntry> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string serverId, IEnumerable<McpToolEntry> tools)
    {
        foreach (var tool in tools)
        {
            _tools[tool.ToolName] = tool with { ServerId = serverId };
        }
    }

    public McpToolEntry? Lookup(string toolName) =>
        _tools.TryGetValue(toolName, out var tool) ? tool : null;

    public IReadOnlyCollection<McpToolEntry> List() => _tools.Values.ToArray();
}
